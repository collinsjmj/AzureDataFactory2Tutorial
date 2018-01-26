using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Rest;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.DataFactory;
using Microsoft.Azure.Management.DataFactory.Models;
using Microsoft.IdentityModel.Clients.ActiveDirectory;

namespace AzureDataFactory2Tutorial
{
    class Program
    {
        static void Main(string[] args)
        {
            // Set variables
            string tenantID = "bb43b24f-1139-4bd9-821c-6cebb99a16cf";
            string applicationId = "f1687a4d-b74f-469c-ae31-9d2ba5ec8822";
            string authenticationKey = "gIwhGAbkezz5jJEqvX9aEwMZQRquWHalvdEs1iwXEf4=";
            string subscriptionId = "7fd8c66d-1713-4ebe-8afe-632f06189a6e"; //subscription for Johnc_MSDN
            string resourceGroup = "ITW.JohnC.Dev";
            string region = "East US 2";
            string dataFactoryName = "NewCo-Dev-JohnC-DF2-tutorial";
            string storageAccount = "itwjohncdevstorage";
            string storageKey = "M7jI749EdOJQxZ2F0M7zkkF4GY/zI0GQ93Tbu1ALfnJFt11R7CV+Pqe+Va1BSPhI36ATLOzXEUpWL/FCrPbm5A==";
            // specify the container and input folder from which all files need to be copied to the output folder. 
            string inputBlobPath = "newco-dev-johnc-tutorial/input";
            //specify the contains and output folder where the files are copied
            string outputBlobPath = "newco-dev-johnc-tutorial/output";

            string storageLinkedServiceName = "AzureStorageLinkedService";  // name of the Azure Storage linked service
            string blobDatasetName = "BlobDataset";             // name of the blob dataset
            string pipelineName = "Adfv2QuickStartPipeline";    // name of the pipeline

            // Authenticate and create a data factory management client
            var context = new AuthenticationContext("https://login.windows.net/" + tenantID);
            ClientCredential cc = new ClientCredential(applicationId, authenticationKey);
            AuthenticationResult result = context.AcquireTokenAsync("https://management.azure.com/", cc).Result;
            ServiceClientCredentials cred = new TokenCredentials(result.AccessToken);
            var client = new DataFactoryManagementClient(cred) { SubscriptionId = subscriptionId };

            // Create a data factory
            Console.WriteLine("Creating data factory " + dataFactoryName + "...");
            Factory dataFactory = new Factory
            {
                Location = region,
                Identity = new FactoryIdentity()
            };
            client.Factories.CreateOrUpdate(resourceGroup, dataFactoryName, dataFactory);
            Console.WriteLine(SafeJsonConvert.SerializeObject(dataFactory, client.SerializationSettings));

            while (client.Factories.Get(resourceGroup, dataFactoryName).ProvisioningState == "PendingCreation")
            {
                System.Threading.Thread.Sleep(1000);
            }


            // Create an Azure Storage linked service
            Console.WriteLine("Creating linked service " + storageLinkedServiceName + "...");

            LinkedServiceResource storageLinkedService = new LinkedServiceResource(
                new AzureStorageLinkedService
                {
                    ConnectionString = new SecureString("DefaultEndpointsProtocol=https;AccountName=" + storageAccount + ";AccountKey=" + storageKey)
                }
            );
            client.LinkedServices.CreateOrUpdate(resourceGroup, dataFactoryName, storageLinkedServiceName, storageLinkedService);
            Console.WriteLine(SafeJsonConvert.SerializeObject(storageLinkedService, client.SerializationSettings));

            // Create a Azure Blob dataset
            Console.WriteLine("Creating dataset " + blobDatasetName + "...");
            DatasetResource blobDataset = new DatasetResource(
                new AzureBlobDataset
                {
                    LinkedServiceName = new LinkedServiceReference
                    {
                        ReferenceName = storageLinkedServiceName
                    },
                    FolderPath = new Expression { Value = "@{dataset().path}" },
                    Parameters = new Dictionary<string, ParameterSpecification>
                    {
            { "path", new ParameterSpecification { Type = ParameterType.String } }

                    }
                }
            );
            client.Datasets.CreateOrUpdate(resourceGroup, dataFactoryName, blobDatasetName, blobDataset);
            Console.WriteLine(SafeJsonConvert.SerializeObject(blobDataset, client.SerializationSettings));

            // Create a pipeline with a copy activity
            Console.WriteLine("Creating pipeline " + pipelineName + "...");
            PipelineResource pipeline = new PipelineResource
            {
                Parameters = new Dictionary<string, ParameterSpecification>
    {
        { "inputPath", new ParameterSpecification { Type = ParameterType.String } },
        { "outputPath", new ParameterSpecification { Type = ParameterType.String } }
    },
                Activities = new List<Activity>
    {
        new CopyActivity
        {
            Name = "CopyFromBlobToBlob",
            Inputs = new List<DatasetReference>
            {
                new DatasetReference()
                {
                    ReferenceName = blobDatasetName,
                    Parameters = new Dictionary<string, object>
                    {
                        { "path", "@pipeline().parameters.inputPath" }
                    }
                }
            },
            Outputs = new List<DatasetReference>
            {
                new DatasetReference
                {
                    ReferenceName = blobDatasetName,
                    Parameters = new Dictionary<string, object>
                    {
                        { "path", "@pipeline().parameters.outputPath" }
                    }
                }
            },
            Source = new BlobSource { },
            Sink = new BlobSink { }
        }
    }
            };
            client.Pipelines.CreateOrUpdate(resourceGroup, dataFactoryName, pipelineName, pipeline);
            Console.WriteLine(SafeJsonConvert.SerializeObject(pipeline, client.SerializationSettings));

            // Create a pipeline run
            Console.WriteLine("Creating pipeline run...");
            Dictionary<string, object> parameters = new Dictionary<string, object>
{
    { "inputPath", inputBlobPath },
    { "outputPath", outputBlobPath }
};
            CreateRunResponse runResponse = client.Pipelines.CreateRunWithHttpMessagesAsync(resourceGroup, dataFactoryName, pipelineName, parameters).Result.Body;
            Console.WriteLine("Pipeline run ID: " + runResponse.RunId);


            // Monitor the pipeline run
            Console.WriteLine("Checking pipeline run status...");
            PipelineRun pipelineRun;
            while (true)
            {
                pipelineRun = client.PipelineRuns.Get(resourceGroup, dataFactoryName, runResponse.RunId);
                Console.WriteLine("Status: " + pipelineRun.Status);
                if (pipelineRun.Status == "InProgress")
                    System.Threading.Thread.Sleep(15000);
                else
                    break;
            }

            // Check the copy activity run details
            Console.WriteLine("Checking copy activity run details...");

            List<ActivityRun> activityRuns = client.ActivityRuns.ListByPipelineRun(
            resourceGroup, dataFactoryName, runResponse.RunId, DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(10)).ToList();
            if (pipelineRun.Status == "Succeeded")
                Console.WriteLine(activityRuns.First().Output);
            else
                Console.WriteLine(activityRuns.First().Error);
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();


        }
    }
}
