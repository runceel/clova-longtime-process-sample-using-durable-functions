
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using ClovaLab.Configurations;
using CEK.CSharp;
using System.Threading.Tasks;
using CEK.CSharp.Models;
using System;

namespace ClovaLab
{
    public static class LabFunctions
    {
        [FunctionName(nameof(Lab))]
        public static async Task<IActionResult> Lab(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequest req,
            [OrchestrationClient] DurableOrchestrationClient client,
            ExecutionContext context,
            ILogger log)
        {
            var config = ConfigurationManager.GetConfiguration(context);
            var clovaClient = new ClovaClient();
            var cekRequest = await clovaClient.GetRequest(
                req.Headers["SignatureCEK"],
                req.Body,
                config.IsSkipRequestValidation
            );

            var cekResponse = new CEKResponse();
            switch (cekRequest.Request.Type)
            {
                case RequestType.LaunchRequest:
                    {
                        // UserId をインスタンス ID として新しい関数を実行
                        await client.StartNewAsync(nameof(LongTimeOrchestrationFunction), cekRequest.Session.User.UserId, null);
                        cekResponse.AddText("時間のかかる処理を実行しました。結果を教えてと聞くと結果を答えます。");
                        cekResponse.ShouldEndSession = false;
                        break;
                    }
                case RequestType.IntentRequest:
                    {
                        switch (cekRequest.Request.Intent.Name)
                        {
                            case "GetResultIntent":
                                // インスタンス ID が UserId の状態を取得
                                var status = await client.GetStatusAsync(cekRequest.Session.User.UserId);
                                // 状態に応じて応答を設定
                                switch (status.RuntimeStatus)
                                {
                                    case OrchestrationRuntimeStatus.Canceled:
                                        cekResponse.AddText("キャンセルされてます");
                                        break;
                                    case OrchestrationRuntimeStatus.Completed:
                                        cekResponse.AddText($"終わりました。結果は{status.Output.ToObject<string>()}です。");
                                        break;
                                    case OrchestrationRuntimeStatus.ContinuedAsNew:
                                        cekResponse.AddText("やり直してます。もうちょっと待ってね。進捗どうですかと聞いてください。");
                                        cekResponse.ShouldEndSession = false;
                                        break;
                                    case OrchestrationRuntimeStatus.Failed:
                                        cekResponse.AddText("失敗しました。");
                                        break;
                                    case OrchestrationRuntimeStatus.Pending:
                                        cekResponse.AddText("もうちょっと待ってね。進捗どうですかと聞いてください。");
                                        cekResponse.ShouldEndSession = false;
                                        break;
                                    case OrchestrationRuntimeStatus.Running:
                                        cekResponse.AddText("もうちょっと待ってね。進捗どうですかと聞いてください。");
                                        cekResponse.ShouldEndSession = false;
                                        break;
                                    case OrchestrationRuntimeStatus.Terminated:
                                        cekResponse.AddText("失敗しました。");
                                        break;
                                }
                                break;
                            default:
                                cekResponse.AddText("すみません。よくわかりませんでした。");
                                break;
                        }
                        break;
                    }
                case RequestType.SessionEndedRequest:
                    {
                        // 途中で終了されたら終わらせておく
                        await client.TerminateAsync(cekRequest.Session.User.UserId, "User canceled");
                        break;
                    }
            }

            return new OkObjectResult(cekResponse);
        }

        [FunctionName(nameof(LongTimeOrchestrationFunction))]
        public static async Task<string> LongTimeOrchestrationFunction(
            [OrchestrationTrigger] DurableOrchestrationContext context)
        {
            // 本当はここで複数個の CallActivityAsync を呼んだりできるけど、ここでは 1 個だけ
            return await context.CallActivityAsync<string>(nameof(LongTimeActivityFunction), null);
        }

        [FunctionName(nameof(LongTimeActivityFunction))]
        public static async Task<string> LongTimeActivityFunction(
            [ActivityTrigger] DurableActivityContext context)
        {
            // 20 秒待って結果を返すだけ。
            // 本当は、外部 API をここで呼び出したりする
            await Task.Delay(20000);
            return DateTimeOffset.UtcNow.ToString("UTCではyyyy年MM月dd日HH時mm分ss秒");
        }
    }
}
