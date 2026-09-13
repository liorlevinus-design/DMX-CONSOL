using DmxConsole.Core.Presets;
using Xunit;

namespace DmxConsole.Core.Tests;

public class PresetLibraryTests
{
    [Fact]
    public void Add_AssignsSequentialNumbers_PerAttributeClass()
    {
        var library = new PresetLibrary();
        var a = new Preset { Class = AttributeClass.Color, Name = "Blue" };
        var b = new Preset { Class = AttributeClass.Color, Name = "Red" };
        var c = new Preset { Class = AttributeClass.Position, Name = "Centre" };

        library.Add(a);
        library.Add(b);
        library.Add(c);

        Assert.Equal(1, a.Number);
        Assert.Equal(2, b.Number);
        Assert.Equal(1, c.Number); // independent numbering per Class
    }

    [Fact]
    public void Add_KeepsExplicitNumber_WhenNotTaken()
    {
        var library = new PresetLibrary();
        var preset = new Preset { Class = AttributeClass.Beam, Number = 7 };

        library.Add(preset);

        Assert.Equal(7, preset.Number);
    }

    [Fact]
    public void Add_ReassignsNumber_WhenExplicitNumberAlreadyTakenInSameClass()
    {
        var library = new PresetLibrary();
        library.Add(new Preset { Class = AttributeClass.Intensity, Number = 3 });
        var second = new Preset { Class = AttributeClass.Intensity, Number = 3 };

        library.Add(second);

        Assert.Equal(4, second.Number);
    }

    [Fact]
    public void Add_SameNumberDifferentClass_DoesNotCollide()
    {
        var library = new PresetLibrary();
        var color = new Preset { Class = AttributeClass.Color, Number = 5 };
        var beam = new Preset { Class = AttributeClass.Beam, Number = 5 };

        library.Add(color);
        library.Add(beam);

        Assert.Equal(5, color.Number);
        Assert.Equal(5, beam.Number);
    }

    [Fact]
    public void FindByNumber_ReturnsMatchWithinClassOnly()
    {
        var library = new PresetLibrary();
        var color = new Preset { Class = AttributeClass.Color, Number = 1, Name = "Color One" };
        var position = new Preset { Class = AttributeClass.Position, Number = 1, Name = "Position One" };
        library.Add(color);
        library.Add(position);

        Assert.Same(color, library.FindByNumber(AttributeClass.Color, 1));
        Assert.Same(position, library.FindByNumber(AttributeClass.Position, 1));
        Assert.Null(library.FindByNumber(AttributeClass.Beam, 1));
    }

    [Fact]
    public void ForClass_OnlyReturnsThatClassesPresets()
    {
        var library = new PresetLibrary();
        library.Add(new Preset { Class = AttributeClass.Color });
        library.Add(new Preset { Class = AttributeClass.Color });
        library.Add(new Preset { Class = AttributeClass.Beam });

        Assert.Equal(2, library.ForClass(AttributeClass.Color).Count());
        Assert.Single(library.ForClass(AttributeClass.Beam));
    }
}
