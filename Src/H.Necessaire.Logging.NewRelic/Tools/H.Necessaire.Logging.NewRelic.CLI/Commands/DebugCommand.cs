using H.Necessaire.Runtime.CLI.Commands;
using System;
using System.Threading.Tasks;

namespace H.Necessaire.Logging.NewRelic.CLI.Commands
{
    internal class DebugCommand : CommandBase
    {
        public override async Task<OperationResult> Run()
        {
            await Logger.LogTrace("Just a Simple Trace");
            await Logger.LogInfo("Just a Simple Info");
            await Logger.LogDebug("Just a Simple Debug");
            await Logger.LogWarn("Just a Simple Warn");

            await Logger.LogError("Plain Exception", new Exception("Exception message"), new { PayloadA = "PayloadAValue" }, "NoteA".NoteAs("Note"));
            await Logger.LogError("Aggregate Exception", new AggregateException("Main Exception Message", new Exception("Exception A"), new Exception("Exception B")), new { PayloadA = "PayloadAValue" }, "NoteA".NoteAs("Note"));
            await Logger.LogError("Operation Exception", new OperationResultException(OperationResult.Fail("Failure Reason", "Failure Comment A", "Failure Comment B", "Failure Comment C")), new { PayloadA = "PayloadAValue" }, "NoteA".NoteAs("Note"));

            Console.ReadLine();

            return OperationResult.Win();
        }
    }
}
