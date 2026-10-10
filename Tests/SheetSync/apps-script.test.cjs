const fs = require('node:fs'), vm = require('node:vm'), assert = require('node:assert/strict');
const rows = { UpgradeStats:[['StatId','Unit'],['damage','Flat']], Nodes:[['NodeId','MaxRank'],['demo',1]], NodeCost:[['NodeId','Rank','Cost'],['demo',1,100]], NodeEffects:[['NodeId','Rank','StatId','Value'],['demo',1,'damage',10]] };
const sheets = Object.fromEntries(Object.entries(rows).map(([name,data])=>[name,{
 getDataRange:()=>({getValues:()=>data.map(r=>[...r]),getDisplayValues:()=>data.map(r=>r.map(String))}),
 getRange:(r,c)=>({setValue:v=>{data[r-1][c-1]=v},getFormula:()=>''})
}]));
const context={SpreadsheetApp:{getActive:()=>({getSheetByName:n=>sheets[n],getName:()=> 'fixture',getId:()=> 'fixture'}),flush:()=>{}},
 LockService:{getDocumentLock:()=>({tryLock:()=>true,releaseLock:()=>{}})},PropertiesService:{getScriptProperties:()=>({getProperty:()=> 'test-only'})},
 ContentService:{createTextOutput:text=>({text,setMimeType(){return this}}),MimeType:{JSON:'json'}}};
vm.createContext(context); vm.runInContext(fs.readFileSync('AppsScript/BlackholeSheetSync.gs','utf8'),context);
const post=body=>JSON.parse(context.doPost({postData:{contents:JSON.stringify(body)}}).text);
assert.equal(post({token:'wrong',action:'read'}).ok,false);
assert.equal(post({token:'test-only',action:'read'}).ok,true);
const update={tab:'NodeCost',keys:{NodeId:'demo',Rank:'1'},column:'Cost',value:120,expected:100};
assert.equal(post({token:'test-only',action:'write',updates:[update],dryRun:true}).ok,true);assert.equal(rows.NodeCost[1][2],100);
assert.equal(post({token:'test-only',action:'write',updates:[update]}).ok,true);assert.equal(rows.NodeCost[1][2],120);
assert.equal(post({token:'test-only',action:'write',updates:[{...update,value:130,expected:999}]}).ok,false);assert.equal(rows.NodeCost[1][2],120);
assert.equal(post({token:'test-only',action:'write',updates:[{...update,column:'NodeId'}]}).ok,false);
console.log('PASS: Apps Script auth, read, dry-run, write, conflict, allowlist');
