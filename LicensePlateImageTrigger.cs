using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using System.Data.SqlClient;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Company.Function
{
    public static class LicensePlateImageTrigger
    {
        [FunctionName("LicensePlateImageTrigger")]
        public static async Task Run([BlobTrigger("vehicle-plate-image/{name}", Connection = "STORAGE_CONNECTION")]Stream myBlob, 
            string name, ILogger log)
        {
            log.LogInformation($"LicensePlateImageTrigger processed blob\n  Name: {name}\n  Size: {myBlob.Length} Bytes");

            try
            {
                // Cognitive Service key and endpoint
                var subscriptionKey = Environment.GetEnvironmentVariable("COMPUTER_VISION_SUBSCRIPTION_KEY");
                var endpoint = Environment.GetEnvironmentVariable("COMPUTER_VISION_ENDPOINT");

                // Process the image
                var imageProcessor = new LicensePlateImageProcessor(endpoint, subscriptionKey, myBlob, log);
                imageProcessor.ReadLicensePlate().Wait();

                // Process the result
                var plateText = imageProcessor.GetAllText();
                var plateStatus = imageProcessor.GetStatus();
                var dateTime = imageProcessor.GetCreatedDateTime();
                log.LogInformation($"License plate result status: {plateStatus}, time: {dateTime}, text:\n{plateText}");                 
            
                // Write successful reult into SQL DB
                // Failed operation is not handled yet
                if(plateStatus != OperationStatusCodes.Succeeded) return;

                var sqlConnStr = Environment.GetEnvironmentVariable("SQLDB_CONNECTION");
                log.LogInformation("Connect to DB...");
                using (SqlConnection conn = new SqlConnection(sqlConnStr))
                {
                    conn.Open();
                    var text = "INSERT INTO VehiclePlate VALUES (1, CAST('" + dateTime +
                        "' AS DATETIME), '" + plateText + "', '" + name + "')";

                    log.LogInformation($"Execute statement: {text} ...");
                    using (SqlCommand cmd = new SqlCommand(text, conn))
                    {
                        // Execute the command and log the # rows affected.
                        var rows = await cmd.ExecuteNonQueryAsync();
                        log.LogInformation($"{rows} rows were inserted");
                    }
                    conn.Close();
                }
            }
            catch(System.Exception ex)
            {
                log.LogError(ex, "LicensePlateImageTrigger");
            }
        }
    }

    public class LicensePlateImageProcessor
    {
        private ComputerVisionClient _client;
        private Stream _imageStream;
        private ILogger _log;
        private ReadOperationResult _results = null;

        public LicensePlateImageProcessor(string endpoint, string key, Stream stream,
            ILogger log)
        {
            _client = new ComputerVisionClient(new ApiKeyServiceClientCredentials(key))
                { Endpoint = endpoint };
            _imageStream = stream;
            _log = log;
        }

        public async Task ReadLicensePlate()
        {
            _log.LogInformation($"Calling Computer Vision to read the license plate...");

            var headers = await _client.ReadInStreamAsync(_imageStream);
            string operationLocation = headers.OperationLocation;

            //Get the operation ID
            const int numberOfCharsInOperationId = 36;
            string operationId = operationLocation.Substring(operationLocation.Length - numberOfCharsInOperationId);

            // Extract the text
            _log.LogInformation($"Reading results...");
            do
            {
                _results = await _client.GetReadResultAsync(Guid.Parse(operationId));
            }
            while((_results.Status == OperationStatusCodes.Running ||
                _results.Status == OperationStatusCodes.NotStarted));
        }

        public string GetAllText()
        {
            string str = "";
            var licensePlateResults = _results.AnalyzeResult.ReadResults;
            foreach(ReadResult page in licensePlateResults)
            {
                foreach(Line line in page.Lines)
                {
                    if(str.Length > 0) str += " ";
                    str += line.Text;
                }
            }
            return str;
        }

        public OperationStatusCodes GetStatus()
        {
            return _results.Status;
        }

        public string GetCreatedDateTime()
        {
            return _results.CreatedDateTime;
        }
    }
}
