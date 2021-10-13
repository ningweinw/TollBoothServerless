using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using System.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.Storage.Blob;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Company.Function
{
    public static class BatchCSVHttpTrigger
    {
        [FunctionName("BatchCSVHttpTrigger")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            [Blob("vehicle-plate-csv", FileAccess.Read, Connection = "STORAGE_CONNECTION")] CloudBlobContainer container,
            ILogger log)
        {
            log.LogInformation("BatchCSVHttpTrigger function processed a request");

            // Number of records from DB
            int numDbRecords = 0;
            try
            {
                // Required input parameters, startTime=yyyy-MM-ddTHH:mm:ss
                string startTimeStr = req.Query["startTime"];
                string numHoursStr = req.Query["numHours"];

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);
                startTimeStr = startTimeStr ?? data?.startTimeStr;
                numHoursStr = numHoursStr ?? data?.numHoursStr;
                log.LogInformation($"Input startTime: {startTimeStr}, numHours: {numHoursStr}");

                // Get Blob
                CloudBlockBlob blob = container.GetBlockBlobReference(startTimeStr + ".csv");
                
                // Query SQL DB
                var sqlConnStr = Environment.GetEnvironmentVariable("SQLDB_CONNECTION");
                log.LogInformation("Connect to DB...");
                using (SqlConnection conn = new SqlConnection(sqlConnStr))
                {
                    conn.Open();
                    var text = "SELECT boothid, CONVERT(VARCHAR, capturetime, 126), platetext " +
                        "FROM VehiclePlate WHERE capturetime >= CAST('" + 
                        startTimeStr + "' AS DATETIME) AND capturetime < DATEADD(HOUR, " + 
                        numHoursStr + ", CAST('" + startTimeStr + "' AS DATETIME))";

                    log.LogInformation($"Execute statement: {text} ...");
                    using (SqlCommand cmd = new SqlCommand(text, conn))
                    {
                        // Execute the command and log the # rows returned
                        log.LogInformation("Read records...");
                        string csv = "";
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                csv += String.Format("{0},{1},{2}\n", reader.GetInt32(0), reader.GetString(1),
                                    reader.GetString(2));
                                ++numDbRecords;
                            }
                        }
                        blob.UploadText(csv);
                        log.LogInformation($"{numDbRecords} rows were returned, CSV output:\n{csv}");
                    }
                    conn.Close();
                }
            }
            catch(System.Exception ex)
            {
                log.LogError(ex, "BatchCSVHttpTrigger");
            }
            BatchCSVResult result = new BatchCSVResult();
            result.numDbRecords = numDbRecords;
            return new JsonResult(result);
        }
    }

    public class BatchCSVResult
    {
        public int numDbRecords {get; set;}
    }
}
