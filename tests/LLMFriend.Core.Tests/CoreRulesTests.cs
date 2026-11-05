using LLMFriend.Core;

namespace LLMFriend.Core.Tests;

public sealed class CoreRulesTests
{
    [Fact]
    public void AccountOwnershipAndEditingAreDistinct()
    {
        Assert.True(AccountAccess.CanManage("owner", "owner", false));
        Assert.True(AccountAccess.CanManage("editor", "owner", true));
        Assert.False(AccountAccess.CanManage("other", "owner", false));
        Assert.False(AccountAccess.CanDelete("editor", "owner"));
        Assert.False(AccountAccess.CanReauthorize("editor", "owner"));
    }

    [Fact]
    public void SnapshotChangeDetectionRecordsMeaningfulChangesOnly()
    {
        var before = new ConnectorSnapshot(new ExternalProfile("1", "alice", "Alice", null),
            [new ExternalGuild("g1", "First", null), new ExternalGuild("g2", "Second", null)]);
        var after = new ConnectorSnapshot(new ExternalProfile("1", "alice", "Alicia", null),
            [new ExternalGuild("g1", "Renamed", null), new ExternalGuild("g3", "Third", null)]);
        var changes = SnapshotChanges.Compare(before, after);
        Assert.Equal(4, changes.Count);
        Assert.Contains(changes, x => x.Kind == "profile");
        Assert.Contains(changes, x => x.Kind == "guild_joined");
        Assert.Contains(changes, x => x.Kind == "guild_left");
        Assert.Contains(changes, x => x.Kind == "guild");
        Assert.Empty(SnapshotChanges.Compare(after, after));
    }

    [Fact]
    public void PkceChallengeMatchesRfc7636Example()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", OAuthSecurity.Challenge(verifier));
        Assert.NotEqual(OAuthSecurity.NewRandomValue(), OAuthSecurity.NewRandomValue());
    }
}
