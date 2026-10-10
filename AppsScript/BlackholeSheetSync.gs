/**
 * 블랙홀 키우기 — 노드 시트 동기화 웹 앱 (밸런스 루프 M5)
 *
 * Unity 에디터(Integration Lab > Sheet Sync)가 이 웹 앱으로 노드 탭 4개를 읽고(read), 승격한 값을 쓴다(write).
 * 설치와 사용법: 레포 Docs/SheetSync.md "설치".
 *
 * - 모든 요청은 POST(JSON 본문)이고, 본문의 token이 스크립트 속성 BH_SYNC_TOKEN과 같아야 한다.
 * - read: 탭을 시트 메뉴 "파일 > 다운로드 > CSV"와 같은 모양으로 돌려준다
 *   (보이는 값, 쉼표·따옴표·줄바꿈이 든 칸만 따옴표, 줄 끝 CRLF, 마지막 줄바꿈 없음, 끝의 빈 행 없음).
 * - write: 키(NodeId·Rank·StatId)로 행을 찾아 NodeCost의 Cost, NodeEffects의 Value 칸만 고친다.
 *   모든 칸을 찾고, 지금 값이 기대값(expected)일 때만 한 번에 쓴다(하나라도 틀리면 아무것도 쓰지 않는다).
 *   이미 새 값이면 쓰지 않고 성공으로 친다. NodeEffects의 표시 칸은 수식이 아니고 이전 값의 표시와 같을 때만 맞춘다.
 * - 이 파일의 이름은 모두 bh로 시작한다. 같은 프로젝트에 doGet·doPost가 이미 있으면 겹치므로 알려 준다.
 */

var BH_TOKEN_KEY = 'BH_SYNC_TOKEN';
var BH_TABS = ['UpgradeStats', 'Nodes', 'NodeCost', 'NodeEffects'];
var BH_KEYS = {
  UpgradeStats: ['StatId'],
  Nodes: ['NodeId'],
  NodeCost: ['NodeId', 'Rank'],
  NodeEffects: ['NodeId', 'Rank', 'StatId'],
};
var BH_WRITABLE = { NodeCost: ['Cost'], NodeEffects: ['Value'] };

// 브라우저로 주소를 열었을 때. 데이터는 주지 않는다.
function doGet() {
  return ContentService.createTextOutput('BlackHole sheet sync: POST(JSON)만 받는다.');
}

function doPost(e) {
  var reply;

  try {
    var body = JSON.parse(e && e.postData && e.postData.contents ? e.postData.contents : '{}');
    var token = PropertiesService.getScriptProperties().getProperty(BH_TOKEN_KEY);

    if (!token) reply = bhFail_('시트에 토큰이 없다. Apps Script에서 bhSyncSetupToken을 한 번 실행한다.');
    else if (body.token !== token) reply = bhFail_('토큰이 맞지 않는다.');
    else if (body.action === 'ping') reply = bhPing_();
    else if (body.action === 'read') reply = bhRead_(body.tabs && body.tabs.length ? body.tabs : BH_TABS);
    else if (body.action === 'write') reply = bhWrite_(body.updates || [], body.dryRun === true);
    else reply = bhFail_('알 수 없는 action: ' + body.action);
  } catch (err) {
    reply = bhFail_(String((err && err.message) || err));
  }

  return ContentService.createTextOutput(JSON.stringify(reply)).setMimeType(ContentService.MimeType.JSON);
}

// 처음 한 번 실행한다(편집기에서 함수 고르고 ▶). 새 토큰을 만들어 저장하고 로그에 보인다. 다시 실행하면 토큰이 바뀐다.
function bhSyncSetupToken() {
  var token = Utilities.getUuid().replace(/-/g, '') + Utilities.getUuid().replace(/-/g, '').slice(0, 8);
  PropertiesService.getScriptProperties().setProperty(BH_TOKEN_KEY, token);
  Logger.log('BlackHole 시트 동기화 토큰: ' + token);
  return token;
}

// 지금 토큰을 로그에 보인다.
function bhSyncShowToken() {
  var token = PropertiesService.getScriptProperties().getProperty(BH_TOKEN_KEY);
  Logger.log(token ? 'BlackHole 시트 동기화 토큰: ' + token : '토큰이 없다. bhSyncSetupToken을 실행한다.');
  return token;
}

function bhPing_() {
  var ss = SpreadsheetApp.getActive();
  var present = [];
  var missing = [];

  BH_TABS.forEach(function (tab) {
    (ss.getSheetByName(tab) ? present : missing).push(tab);
  });

  return { ok: true, sheet: ss.getName(), id: ss.getId(), tabs: present, missing: missing, at: new Date().toISOString() };
}

function bhRead_(tabs) {
  var ss = SpreadsheetApp.getActive();
  var out = {};

  for (var i = 0; i < tabs.length; i++) {
    var sheet = ss.getSheetByName(tabs[i]);

    if (!sheet) return bhFail_('탭이 없다: ' + tabs[i]);

    var rows = sheet.getDataRange().getDisplayValues();

    // 수식·서식이 아래로 늘어 있으면 빈 행까지 읽힌다. 시트의 CSV 다운로드처럼 끝의 빈 행은 뺀다.
    while (rows.length > 1 && rows[rows.length - 1].every(function (cell) { return String(cell).trim() === ''; }))
      rows.pop();

    out[tabs[i]] = rows.map(function (row) { return row.map(bhCsvCell_).join(','); }).join('\r\n');
  }

  return { ok: true, tabs: out, at: new Date().toISOString() };
}

// updates: [{ tab, keys: { NodeId, Rank, StatId? }, column, value, expected? }]
function bhWrite_(updates, dryRun) {
  var lock = LockService.getDocumentLock();

  if (!lock.tryLock(10000)) return bhFail_('다른 쓰기가 진행 중이다. 잠시 뒤 다시 한다.');

  try {
    var ss = SpreadsheetApp.getActive();
    var tables = {};
    var plan = [];
    var errors = [];

    updates.forEach(function (u, i) {
      var where = 'updates[' + i + '] ' + u.tab + ' ' + JSON.stringify(u.keys) + ' ' + u.column;

      if (!BH_WRITABLE[u.tab] || BH_WRITABLE[u.tab].indexOf(u.column) < 0) {
        errors.push(where + ': 이 칸은 쓰지 않는다(NodeCost.Cost, NodeEffects.Value만).');
        return;
      }

      if (typeof u.value !== 'number' || !isFinite(u.value)) {
        errors.push(where + ': 값이 숫자가 아니다.');
        return;
      }

      var table = tables[u.tab] || (tables[u.tab] = bhTable_(ss, u.tab));

      if (table.error) {
        errors.push(where + ': ' + table.error);
        return;
      }

      var found = bhFindRow_(table, BH_KEYS[u.tab], u.keys);

      if (found.error) {
        errors.push(where + ': ' + found.error);
        return;
      }

      var col = table.header.indexOf(u.column);
      var before = bhNumber_(table.values[found.row][col]);

      if (isNaN(before)) {
        errors.push(where + ': 지금 값이 숫자가 아니다(' + table.display[found.row][col] + ').');
        return;
      }

      var already = bhSame_(before, u.value);

      if (!already && u.expected !== undefined && u.expected !== null && !bhSame_(before, u.expected)) {
        errors.push(where + ': 시트 값이 ' + before + '이다(기대 ' + u.expected + '). 시트에서 바뀌었다.');
        return;
      }

      plan.push({ u: u, table: table, row: found.row, col: col, before: before, already: already });
    });

    if (errors.length > 0)
      return { ok: false, error: errors.length + '개 칸을 쓸 수 없어 아무것도 바꾸지 않았다.', errors: errors };

    if (!dryRun) {
      plan.forEach(function (p) {
        if (p.already) return;

        p.table.sheet.getRange(p.row + 1, p.col + 1).setValue(p.u.value);
        bhFixDisplay_(p);
      });

      SpreadsheetApp.flush();
    }

    return {
      ok: true,
      dryRun: dryRun,
      results: plan.map(function (p) {
        return { tab: p.u.tab, keys: p.u.keys, column: p.u.column, row: p.row + 1, before: p.before, after: p.u.value, already: p.already };
      }),
    };
  } finally {
    lock.releaseLock();
  }
}

// NodeEffects의 표시 칸("+25%")이 수식이 아니고 이전 값의 표시와 같으면 새 값으로 맞춘다.
function bhFixDisplay_(p) {
  var table = p.table;
  var displayCol = table.header.indexOf('표시');
  var unitCol = table.header.indexOf('단위');

  if (p.u.tab !== 'NodeEffects' || displayCol < 0 || unitCol < 0) return;

  var cell = table.sheet.getRange(p.row + 1, displayCol + 1);
  var unit = table.display[p.row][unitCol];

  if (cell.getFormula() !== '' || table.display[p.row][displayCol] !== bhDisplay_(p.before, unit)) return;

  // 글자로 남긴다("+12.5%"를 숫자 0.125로 바꾸지 않게).
  cell.setValue("'" + bhDisplay_(p.u.value, unit));
}

function bhTable_(ss, tab) {
  var sheet = ss.getSheetByName(tab);

  if (!sheet) return { error: '탭이 없다: ' + tab };

  var range = sheet.getDataRange();
  var display = range.getDisplayValues();
  var header = (display[0] || []).map(function (h) { return String(h).trim(); });

  return { sheet: sheet, values: range.getValues(), display: display, header: header };
}

function bhFindRow_(table, keyColumns, keys) {
  var cols = [];

  for (var k = 0; k < keyColumns.length; k++) {
    var c = table.header.indexOf(keyColumns[k]);

    if (c < 0) return { error: '머리칸이 없다: ' + keyColumns[k] };
    if (keys[keyColumns[k]] === undefined) return { error: '키가 빠졌다: ' + keyColumns[k] };

    cols.push(c);
  }

  var found = -1;

  for (var r = 1; r < table.display.length; r++) {
    var match = true;

    for (var j = 0; j < cols.length && match; j++)
      match = String(table.display[r][cols[j]]).trim() === String(keys[keyColumns[j]]);

    if (!match) continue;
    if (found >= 0) return { error: '같은 행이 여러 개다(' + (found + 1) + '행, ' + (r + 1) + '행).' };

    found = r;
  }

  return found < 0 ? { error: '행이 없다.' } : { row: found };
}

function bhCsvCell_(value) {
  var text = String(value);
  return /[",\r\n]/.test(text) ? '"' + text.replace(/"/g, '""') + '"' : text;
}

function bhNumber_(value) {
  return typeof value === 'number' ? value : Number(String(value).replace(/,/g, '').trim());
}

function bhSame_(a, b) {
  return Math.abs(a - b) <= 1e-6 * Math.max(1, Math.abs(a), Math.abs(b));
}

// 표시 칸 규칙(레포 NodeSheetEdits.DisplayOf와 같다): 부호 + 값 + (Percent면 %).
function bhDisplay_(value, unit) {
  return (value < 0 ? '' : '+') + bhDecimal_(value) + (unit === 'Percent' ? '%' : '');
}

function bhDecimal_(value) {
  return String(Math.round(value * 1e12) / 1e12);
}

function bhFail_(message) {
  return { ok: false, error: message };
}
