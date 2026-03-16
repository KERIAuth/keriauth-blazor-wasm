using System.Text.Json;
using System.Text.Json.Serialization;
using Extension.Models.Messages.AppBw;

namespace Extension.Tests.Models {
    public class IpexWorkflowTests {
        private readonly JsonSerializerOptions _jsonOptions = new() {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        [Theory]
        [InlineData(IpexWorkflow.Apply, "\"Apply\"")]
        [InlineData(IpexWorkflow.ApplyOffer, "\"ApplyOffer\"")]
        [InlineData(IpexWorkflow.ApplyOfferAgree, "\"ApplyOfferAgree\"")]
        [InlineData(IpexWorkflow.ApplyOfferAgreeGrant, "\"ApplyOfferAgreeGrant\"")]
        [InlineData(IpexWorkflow.ApplyOfferAgreeGrantAdmit, "\"ApplyOfferAgreeGrantAdmit\"")]
        [InlineData(IpexWorkflow.Grant, "\"Grant\"")]
        [InlineData(IpexWorkflow.GrantAdmit, "\"GrantAdmit\"")]
        public void IpexWorkflow_SerializesAsString(IpexWorkflow workflow, string expectedJson) {
            var json = JsonSerializer.Serialize(workflow, _jsonOptions);
            Assert.Equal(expectedJson, json);
        }

        [Theory]
        [InlineData("\"Apply\"", IpexWorkflow.Apply)]
        [InlineData("\"ApplyOffer\"", IpexWorkflow.ApplyOffer)]
        [InlineData("\"Grant\"", IpexWorkflow.Grant)]
        [InlineData("\"GrantAdmit\"", IpexWorkflow.GrantAdmit)]
        public void IpexWorkflow_DeserializesFromString(string json, IpexWorkflow expected) {
            var result = JsonSerializer.Deserialize<IpexWorkflow>(json, _jsonOptions);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void PrimeDataIpexPayload_RoundTrip() {
            var payload = new PrimeDataIpexPayload(
                DiscloserPrefix: "EHMnCf8_nIemuPx-cUHb1k5DsT8K09vqx0bSwNRr9S4c",
                DiscloseePrefix: "EKE3-w61B11vVODLHZdH52zLXoxw6xE3tVv__wfAXN6c",
                Workflow: IpexWorkflow.ApplyOfferAgreeGrantAdmit,
                EcrRole: "Project Manager",
                IsPresentation: false
            );

            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            var deserialized = JsonSerializer.Deserialize<PrimeDataIpexPayload>(json, _jsonOptions);

            Assert.NotNull(deserialized);
            Assert.Equal(payload.DiscloserPrefix, deserialized.DiscloserPrefix);
            Assert.Equal(payload.DiscloseePrefix, deserialized.DiscloseePrefix);
            Assert.Equal(payload.Workflow, deserialized.Workflow);
            Assert.Equal(payload.EcrRole, deserialized.EcrRole);
            Assert.Equal(payload.IsPresentation, deserialized.IsPresentation);
        }

        [Fact]
        public void PrimeDataIpexPayload_Presentation_RoundTrip() {
            var payload = new PrimeDataIpexPayload(
                DiscloserPrefix: "EHMnCf8_nIemuPx-cUHb1k5DsT8K09vqx0bSwNRr9S4c",
                DiscloseePrefix: "EKE3-w61B11vVODLHZdH52zLXoxw6xE3tVv__wfAXN6c",
                Workflow: IpexWorkflow.Grant,
                EcrRole: "Auditor",
                IsPresentation: true
            );

            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            var deserialized = JsonSerializer.Deserialize<PrimeDataIpexPayload>(json, _jsonOptions);

            Assert.NotNull(deserialized);
            Assert.True(deserialized.IsPresentation);
            Assert.Equal(IpexWorkflow.Grant, deserialized.Workflow);
        }

        [Fact]
        public void PrimeDataIpexResponse_Success() {
            var response = new PrimeDataIpexResponse(true);
            var json = JsonSerializer.Serialize(response, _jsonOptions);
            var deserialized = JsonSerializer.Deserialize<PrimeDataIpexResponse>(json, _jsonOptions);

            Assert.NotNull(deserialized);
            Assert.True(deserialized.Success);
            Assert.Null(deserialized.Error);
        }

        [Fact]
        public void PrimeDataIpexResponse_Error() {
            var response = new PrimeDataIpexResponse(false, Error: "Discloser does not hold ECR Auth credential");
            var json = JsonSerializer.Serialize(response, _jsonOptions);
            var deserialized = JsonSerializer.Deserialize<PrimeDataIpexResponse>(json, _jsonOptions);

            Assert.NotNull(deserialized);
            Assert.False(deserialized.Success);
            Assert.Equal("Discloser does not hold ECR Auth credential", deserialized.Error);
        }

        [Fact]
        public void IpexEligibleDisclosersPayload_RoundTrip() {
            var payload = new IpexEligibleDisclosersPayload(
                IsPresentation: true,
                Workflow: IpexWorkflow.ApplyOffer
            );

            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            var deserialized = JsonSerializer.Deserialize<IpexEligibleDisclosersPayload>(json, _jsonOptions);

            Assert.NotNull(deserialized);
            Assert.True(deserialized.IsPresentation);
            Assert.Equal(IpexWorkflow.ApplyOffer, deserialized.Workflow);
        }

        [Fact]
        public void IpexEligibleDisclosersResponse_WithPrefixes() {
            var response = new IpexEligibleDisclosersResponse(
                true,
                ["EHMnCf8_prefix1", "EKE3-prefix2"]
            );

            var json = JsonSerializer.Serialize(response, _jsonOptions);
            var deserialized = JsonSerializer.Deserialize<IpexEligibleDisclosersResponse>(json, _jsonOptions);

            Assert.NotNull(deserialized);
            Assert.True(deserialized.Success);
            Assert.Equal(2, deserialized.Prefixes.Count);
            Assert.Null(deserialized.Error);
        }

        [Theory]
        [InlineData(nameof(AppBwMessageType.Values.RequestPrimeDataIpex))]
        [InlineData(nameof(AppBwMessageType.Values.RequestIpexEligibleDisclosers))]
        public void AppBwMessageType_TryParse_NewTypes(string fieldName) {
            var value = fieldName switch {
                nameof(AppBwMessageType.Values.RequestPrimeDataIpex) => AppBwMessageType.Values.RequestPrimeDataIpex,
                nameof(AppBwMessageType.Values.RequestIpexEligibleDisclosers) => AppBwMessageType.Values.RequestIpexEligibleDisclosers,
                _ => throw new ArgumentException($"Unknown field: {fieldName}")
            };

            Assert.True(AppBwMessageType.TryParse(value, out var result));
            Assert.Equal(value, result.Value);
        }
    }
}
