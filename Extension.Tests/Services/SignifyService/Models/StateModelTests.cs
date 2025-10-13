using Extension.Services.SignifyService.Models;
using System.Text.Json;
using Xunit;

namespace Extension.Tests.Services.SignifyService.Models;

/// <summary>
/// Tests for State model serialization/deserialization.
/// These tests ensure compatibility with signify-ts TypeScript interfaces.
/// </summary>
public class StateModelTests {
    private readonly JsonSerializerOptions _jsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void State_Deserialization_WithValidJson_ShouldSucceed() {
        // Arrange - JSON similar to what signify-ts returns
        const string json = """
            {
                "agent": {
                    "i": "EALkveIFUPvt38xhtgYYJRCCpAGO7WjjHVR37Pawv67E"
                },
                "controller": {
                    "state": {
                        "i": "EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u",
                        "s": "0",
                        "k": ["DKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u"]
                    }
                },
                "ridx": 0,
                "pidx": 0
            }
            """;

        // Act
        var state = JsonSerializer.Deserialize<State>(json, _jsonOptions);

        // Assert
        Assert.NotNull(state);
        Assert.NotNull(state.Agent);
        Assert.NotNull(state.Controller);
        Assert.Equal("EALkveIFUPvt38xhtgYYJRCCpAGO7WjjHVR37Pawv67E", state.Agent.I);
        Assert.Equal("EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u", state.Controller.State?.I);
        Assert.Equal(0, state.Ridx);
        Assert.Equal(0, state.Pidx);
    }

    [Fact]
    public void State_Serialization_ShouldProduceValidJson() {
        // Arrange
        var agent = new Agent { I = "EALkveIFUPvt38xhtgYYJRCCpAGO7WjjHVR37Pawv67E" };
        var controllerState = new ControllerState {
            I = "EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u",
            S = "0",
            K = ["DKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u"]
        };
        var controller = new Controller { State = controllerState };
        var state = new State {
            Agent = agent,
            Controller = controller,
            Ridx = 0,
            Pidx = 0
        };

        // Act
        var json = JsonSerializer.Serialize(state, _jsonOptions);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("agent", json);
        Assert.Contains("controller", json);
        Assert.Contains("ridx", json);
        Assert.Contains("pidx", json);
    }

    [Fact]
    public void State_WithNullAgent_ShouldDeserializeCorrectly() {
        // Arrange
        const string json = """
            {
                "agent": null,
                "controller": {
                    "state": {
                        "i": "EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u"
                    }
                },
                "ridx": 0,
                "pidx": 0
            }
            """;

        // Act
        var state = JsonSerializer.Deserialize<State>(json, _jsonOptions);

        // Assert
        Assert.NotNull(state);
        Assert.Null(state.Agent);
        Assert.NotNull(state.Controller);
    }

    [Fact]
    public void State_WithNullController_ShouldDeserializeCorrectly() {
        // Arrange
        const string json = """
            {
                "agent": {
                    "i": "EALkveIFUPvt38xhtgYYJRCCpAGO7WjjHVR37Pawv67E"
                },
                "controller": null,
                "ridx": 0,
                "pidx": 0
            }
            """;

        // Act
        var state = JsonSerializer.Deserialize<State>(json, _jsonOptions);

        // Assert
        Assert.NotNull(state);
        Assert.NotNull(state.Agent);
        Assert.Null(state.Controller);
    }

    [Fact]
    public void ControllerState_WithComplexData_ShouldDeserializeCorrectly() {
        // Arrange - JSON with all ControllerState fields
        const string json = """
            {
                "vn": [1, 0],
                "i": "EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u",
                "s": "1",
                "p": "EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u",
                "d": "EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u",
                "f": "1",
                "dt": "2023-01-01T00:00:00Z",
                "et": "rot",
                "kt": "1",
                "k": ["DKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u"],
                "nt": "1",
                "n": ["DKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u"],
                "bt": "0",
                "b": [],
                "c": [],
                "ee": {
                    "s": "1",
                    "d": "EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u",
                    "br": [],
                    "ba": []
                },
                "di": ""
            }
            """;

        // Act
        var controllerState = JsonSerializer.Deserialize<ControllerState>(json, _jsonOptions);

        // Assert
        Assert.NotNull(controllerState);
        Assert.Equal([1, 0], controllerState.Vn);
        Assert.Equal("EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u", controllerState.I);
        Assert.Equal("1", controllerState.S);
        Assert.Equal("rot", controllerState.Et);
        Assert.Single(controllerState.K);
        Assert.NotNull(controllerState.Ee);
        Assert.Equal("1", controllerState.Ee.S);
    }

    [Fact]
    public void StateEe_ShouldHandleEmptyArrays() {
        // Arrange
        const string json = """
            {
                "s": "1",
                "d": "EKYOFIz1dv1P2rW2yDlYgHIyS0fV-f0b1b2y3z4x5v6u",
                "br": [],
                "ba": []
            }
            """;

        // Act
        var stateEe = JsonSerializer.Deserialize<StateEe>(json, _jsonOptions);

        // Assert
        Assert.NotNull(stateEe);
        Assert.Equal("1", stateEe.S);
        Assert.Empty(stateEe.Br);
        Assert.Empty(stateEe.Ba);
    }

    [Fact]
    public void State_RoundTripSerialization_ShouldPreserveData() {
        // Arrange
        var originalState = new State {
            Agent = new Agent { I = "agent-prefix" },
            Controller = new Controller {
                State = new ControllerState {
                    I = "controller-prefix",
                    K = ["key1", "key2"]
                }
            },
            Ridx = 5,
            Pidx = 10
        };

        // Act
        var json = JsonSerializer.Serialize(originalState, _jsonOptions);
        var deserializedState = JsonSerializer.Deserialize<State>(json, _jsonOptions);

        // Assert
        Assert.NotNull(deserializedState);
        Assert.Equal(originalState.Agent?.I, deserializedState.Agent?.I);
        Assert.Equal(originalState.Controller?.State?.I, deserializedState.Controller?.State?.I);
        Assert.Equal(originalState.Ridx, deserializedState.Ridx);
        Assert.Equal(originalState.Pidx, deserializedState.Pidx);
        Assert.Equal(originalState.Controller?.State?.K, deserializedState.Controller?.State?.K);
    }

    [Fact]
    public void ControllerEe_WithAllFields_ShouldSerializeCorrectly() {
        // Arrange
        var controllerEe = new ControllerEe {
            V = "KERI10JSON000001_",
            T = "icp",
            D = "prefix",
            I = "prefix",
            S = "0",
            Kt = "1",
            K = ["key"],
            Nt = "1",
            N = ["next"],
            Bt = "0",
            B = [],
            C = [],
            A = []
        };

        // Act
        var json = JsonSerializer.Serialize(controllerEe, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ControllerEe>(json, _jsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(controllerEe.V, deserialized.V);
        Assert.Equal(controllerEe.T, deserialized.T);
        Assert.Equal(controllerEe.K, deserialized.K);
        Assert.Equal(controllerEe.N, deserialized.N);
    }
}
