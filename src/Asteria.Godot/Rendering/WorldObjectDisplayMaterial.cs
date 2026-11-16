using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// Camera/hand previews use the world mesh's per-surface material identities
/// without MultiMesh instance light/custom data. Copies are owned by the
/// display MeshInstance3D and never mutate cached world batch materials.
/// </summary>
internal static class WorldObjectDisplayMaterial
{
    public static void SetTint(MeshInstance3D instance, Color tint)
    {
        var mesh = instance.Mesh ??
            throw new InvalidOperationException("Object display requires a mesh.");
        for (var i = 0; i < mesh.GetSurfaceCount(); i++)
        {
            var original = mesh.SurfaceGetMaterial(i) ??
                throw new InvalidDataException($"Object mesh surface {i} has no material.");
            var material = instance.GetSurfaceOverrideMaterial(i);
            if (material is null)
            {
                material = (Material)original.Duplicate();
                // Shadow reception belongs to placed world Objects only,
                // never a held item, character portrait or placement ghost.
                material.NextPass = null;
                instance.SetSurfaceOverrideMaterial(i, material);
            }

            switch (material)
            {
                case ShaderMaterial shader:
                    shader.SetShaderParameter("preview_mode", true);
                    shader.SetShaderParameter("preview_tint",
                        new Vector3(tint.R, tint.G, tint.B));
                    break;
                case StandardMaterial3D standard
                    when original is StandardMaterial3D baseMaterial:
                {
                    var c = baseMaterial.AlbedoColor;
                    standard.AlbedoColor = new Color(
                        c.R * tint.R, c.G * tint.G, c.B * tint.B, c.A);
                    break;
                }
                default:
                    throw new InvalidDataException(
                        $"Unsupported Object display material {material.GetType().Name}.");
            }
        }
    }
}
