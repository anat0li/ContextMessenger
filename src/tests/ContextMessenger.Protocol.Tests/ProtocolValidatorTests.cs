using ContextMessenger.Protocol;
using ContextMessenger.Protocol.Wire;

namespace ContextMessenger.Protocol.Tests;

public sealed class ProtocolValidatorTests
{
    private static ContextRequest ValidRequest() => new()
    {
        Version = ProtocolValidator.CurrentVersion,
        Id = "abc-123",
        Commands = [new ContextCommand { Type = "tree" }],
    };

    [Fact]
    public void Validate_accepts_well_formed_request()
    {
        ProtocolValidator.Validate(ValidRequest());
    }

    [Fact]
    public void Validate_accepts_well_formed_batch()
    {
        ProtocolValidator.Validate([ValidRequest()]);
    }

    [Fact]
    public void Validate_throws_for_empty_batch()
    {
        var ex = Assert.Throws<ProtocolException>(() => ProtocolValidator.Validate([]));
        Assert.Equal(ProtocolErrorCodes.EmptyBatch, ex.Code);
    }

    [Fact]
    public void Validate_throws_for_unsupported_version()
    {
        var req = ValidRequest();
        req.Version = Version.Parse("2.0");
        var ex = Assert.Throws<ProtocolException>(() => ProtocolValidator.Validate(req));
        Assert.Equal(ProtocolErrorCodes.InvalidVersion, ex.Code);
    }

    [Fact]
    public void Validate_throws_for_missing_id()
    {
        var req = ValidRequest();
        req.Id = "";
        var ex = Assert.Throws<ProtocolException>(() => ProtocolValidator.Validate(req));
        Assert.Equal(ProtocolErrorCodes.MissingId, ex.Code);
    }

    [Fact]
    public void Validate_throws_for_whitespace_id()
    {
        var req = ValidRequest();
        req.Id = "   ";
        var ex = Assert.Throws<ProtocolException>(() => ProtocolValidator.Validate(req));
        Assert.Equal(ProtocolErrorCodes.MissingId, ex.Code);
    }

    [Fact]
    public void Validate_throws_for_empty_commands()
    {
        var req = ValidRequest();
        req.Commands = [];
        var ex = Assert.Throws<ProtocolException>(() => ProtocolValidator.Validate(req));
        Assert.Equal(ProtocolErrorCodes.EmptyCommandSet, ex.Code);
    }

    [Fact]
    public void Validate_throws_for_command_missing_type()
    {
        var req = ValidRequest();
        req.Commands = [new ContextCommand { Type = "" }];
        var ex = Assert.Throws<ProtocolException>(() => ProtocolValidator.Validate(req));
        Assert.Equal(ProtocolErrorCodes.MissingCommandType, ex.Code);
    }

    [Fact]
    public void Validate_reports_index_of_offending_command()
    {
        var req = ValidRequest();
        req.Commands = [
            new ContextCommand { Type = "tree" },
            new ContextCommand { Type = "" },
        ];
        var ex = Assert.Throws<ProtocolException>(() => ProtocolValidator.Validate(req));
        Assert.Equal(ProtocolErrorCodes.MissingCommandType, ex.Code);
        Assert.Contains("index 1", ex.Message);
    }

    [Fact]
    public void Validate_throws_ArgumentNullException_for_null_request()
    {
        Assert.Throws<ArgumentNullException>(() => ProtocolValidator.Validate((ContextRequest)null!));
    }
}
