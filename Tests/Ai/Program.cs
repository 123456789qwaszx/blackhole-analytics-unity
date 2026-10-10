using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using IntegrationLab;
class Program
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Main()
    {
        var sampleRoot = Path.GetFullPath("Samples/Ai/context.json");
        var root = Path.Combine(Path.GetTempPath(), "ai-lab-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Environment.CurrentDirectory = root;
        Directory.CreateDirectory("Samples/Ai"); File.Copy(sampleRoot,"Samples/Ai/context.json");
        string id = AiLabContext.SaveSample("reduce density");
        string valid = "{\"name\":\"ai-draft\",\"patches\":[{\"path\":\"spawn/count\",\"value\":80,\"reason\":\"less dense\",\"noteIds\":[\""+id+"\"]}]}";
        File.WriteAllText(AiRun.DraftPath,valid);
        var errors=new List<string>();var warnings=new List<string>();
        AiLabContext.Validate("sample",id,AiRun.DraftPath,errors,warnings);Check(errors.Count==0,"valid draft");
        File.WriteAllText(AiRun.DraftPath,valid.Replace("spawn/count","unknown"));errors.Clear();
        AiLabContext.Validate("sample",id,AiRun.DraftPath,errors,warnings);Check(errors.Count>0,"unknown path rejected");
        File.WriteAllText(AiRun.DraftPath,valid.Replace(":80",":80.5"));errors.Clear();
        AiLabContext.Validate("sample",id,AiRun.DraftPath,errors,warnings);Check(errors.Count>0,"integer constraint");
        File.WriteAllText(AiRun.DraftPath,valid.Replace(":80",":1001"));errors.Clear();
        AiLabContext.Validate("sample",id,AiRun.DraftPath,errors,warnings);Check(errors.Count>0,"range constraint");
        Check(!AiRun.Arguments(new AiRunSettings()).Contains("Bash"),"no shell tool");
        Check(AiRun.Arguments(new AiRunSettings()).Contains("Edit(LabData/ai-draft.json)"),"single draft grant");
        var request=new AiRunRequest { Id="test-run", AtUtc=DateTime.UtcNow, SetupKey="sample", NoteId=id, Command="/bin/true" };
        string script=AiRun.Prepare(root,request,new AiRunSettings(),false,null);
        Check(File.ReadAllText(script).Contains("LabData"),"standalone script");
        var record=AiRun.Evaluate(request,DateTime.UtcNow,0,"{\"result\":\"done\"}","",true,new List<string>(),new List<string>(),null);
        Check(record.Status==AiRun.Ok,"successful run classification");
        var failed=AiRun.Evaluate(request,DateTime.UtcNow,1,"{\"is_error\":true}","failure",false,null,null,null);
        Check(failed.Status==AiRun.Error,"failed run classification");
        Console.WriteLine("PASS: draft contract, path/range/integer validation, permissions, script generation, result classification");
    }
}
