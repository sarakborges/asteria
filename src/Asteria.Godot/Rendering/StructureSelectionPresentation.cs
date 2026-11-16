using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>One session-scoped line mesh for the Architects Compass preview.</summary>
public sealed class StructureSelectionPresentation
{
    private readonly MeshInstance3D _visual;
    private StructureSelectionBounds? _shown;

    public StructureSelectionPresentation(Node3D parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest = true,
            AlbedoColor = new Color(0.72f, 0.58f, 0.98f, 1f),
        };
        _visual = new MeshInstance3D
        {
            Name = "ArchitectsCompassSelection",
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = material,
        };
        parent.AddChild(_visual);
    }

    public void Sync(StructureSelectionBounds? selection)
    {
        if (selection is not { } bounds)
        {
            Hide();
            return;
        }
        if (_shown == bounds) return;
        var min = bounds.Minimum;
        var max = bounds.Maximum;
        var a = new Vector3(min.X, min.Y, min.Z);
        var b = new Vector3(max.X + 1f, max.Y + 1f, max.Z + 1f);
        var corners = new[]
        {
            new Vector3(a.X,a.Y,a.Z), new Vector3(b.X,a.Y,a.Z),
            new Vector3(a.X,b.Y,a.Z), new Vector3(b.X,b.Y,a.Z),
            new Vector3(a.X,a.Y,b.Z), new Vector3(b.X,a.Y,b.Z),
            new Vector3(a.X,b.Y,b.Z), new Vector3(b.X,b.Y,b.Z)
        };
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        foreach (var (one, two) in new (int, int)[]
        {
            (0,1),(0,2),(0,4),(1,3),(1,5),(2,3),
            (2,6),(3,7),(4,5),(4,6),(5,7),(6,7)
        })
        {
            mesh.SurfaceAddVertex(corners[one]);
            mesh.SurfaceAddVertex(corners[two]);
        }
        mesh.SurfaceEnd();
        _visual.Mesh = mesh;
        _visual.Visible = true;
        _shown = bounds;
    }

    public void Hide()
    {
        if (_shown is null) return;
        _visual.Visible = false;
        _visual.Mesh = null;
        _shown = null;
    }
}
