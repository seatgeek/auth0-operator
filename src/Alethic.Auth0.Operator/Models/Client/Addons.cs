using System.Collections;
using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Client
{

    public class Addons
    {

        [JsonPropertyName("aws")]
        public IDictionary? AmazonWebServices { get; set; }

        [JsonPropertyName("wams")]
        public IDictionary? AzureMobileServices { get; set; }

        [JsonPropertyName("azure_sb")]
        public IDictionary? AzureServiceBus { get; set; }

        [JsonPropertyName("box")]
        public IDictionary? Box { get; set; }

        [JsonPropertyName("cloudbees")]
        public IDictionary? CloudBees { get; set; }

        [JsonPropertyName("concur")]
        public IDictionary? Concur { get; set; }

        [JsonPropertyName("dropbox")]
        public IDictionary? DropBox { get; set; }

        [JsonPropertyName("echosign")]
        public IDictionary? EchoSign { get; set; }

        [JsonPropertyName("egnyte")]
        public IDictionary? Egnyte { get; set; }

        [JsonPropertyName("firebase")]
        public IDictionary? FireBase { get; set; }

        [JsonPropertyName("newrelic")]
        public IDictionary? NewRelic { get; set; }

        [JsonPropertyName("office365")]
        public IDictionary? Office365 { get; set; }

        [JsonPropertyName("salesforce")]
        public IDictionary? SalesForce { get; set; }

        [JsonPropertyName("salesforce_api")]
        public IDictionary? SalesForceApi { get; set; }

        [JsonPropertyName("salesforce_sandbox_api")]
        public IDictionary? SalesForceSandboxApi { get; set; }

        [JsonPropertyName("samlp")]
        public IDictionary? SamlP { get; set; }

        [JsonPropertyName("sap_api")]
        public IDictionary? SapApi { get; set; }

        [JsonPropertyName("sharepoint")]
        public IDictionary? SharePoint { get; set; }

        [JsonPropertyName("springcm")]
        public IDictionary? SpringCM { get; set; }

        [JsonPropertyName("webapi")]
        public IDictionary? WebApi { get; set; }

        [JsonPropertyName("wsfed")]
        public IDictionary? WsFed { get; set; }

        [JsonPropertyName("zendesk")]
        public IDictionary? Zendesk { get; set; }

        [JsonPropertyName("zoom")]
        public IDictionary? Zoom { get; set; }

    }

}
