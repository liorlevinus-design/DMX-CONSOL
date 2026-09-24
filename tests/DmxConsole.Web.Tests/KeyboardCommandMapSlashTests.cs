using DmxConsole.Application.CommandSurface;
using DmxConsole.Web.CommandSurface;
using Xunit;

namespace DmxConsole.Web.Tests;

/// <summary>Cue TIME split syntax slice: the physical "/" key must resolve to the SAME
/// CommandTokenKind.Slash token CommandComposer's Cue-timing grammar already consumes - no new
/// token kind, no parallel parser, just a keyboard-map entry (KeyboardCommandMap's own doc
/// comment: "the single source of truth mapping a physical keyboard key ... to the same
/// CommandToken a touch button ... would emit").</summary>
public class KeyboardCommandMapSlashTests
{
    [Fact]
    public void ForwardSlashKey_ResolvesToSlashToken()
    {
        var resolved = KeyboardCommandMap.TryResolve("/", out var binding);

        Assert.True(resolved);
        Assert.Null(binding.Digit);
        Assert.Equal(CommandTokenKind.Slash, binding.TokenKind);
    }
}
