using Godot;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Game;

/// <summary>
/// Draws the simulation's terrain as a coloured mesh plus a water plane. Only reads from Sim.
/// </summary>
public partial class TerrainView : Node3D
{
	private static readonly Color DeepWater = new(0.10f, 0.22f, 0.35f);
	private static readonly Color Sand = new(0.76f, 0.70f, 0.50f);
	private static readonly Color Grass = new(0.30f, 0.48f, 0.22f);
	private static readonly Color Forest = new(0.18f, 0.33f, 0.16f);
	private static readonly Color Rock = new(0.45f, 0.42f, 0.38f);
	private static readonly Color Snow = new(0.92f, 0.93f, 0.95f);

	private Terrain? _terrain;

	public void Build(Terrain terrain)
	{
		_terrain = terrain;

		foreach (var child in GetChildren())
		{
			child.QueueFree();
		}

		AddChild(new MeshInstance3D
		{
			Name = "Ground",
			Mesh = BuildGroundMesh(terrain),
			MaterialOverride = new StandardMaterial3D
			{
				VertexColorUseAsAlbedo = true,
				VertexColorIsSrgb = true,
				Roughness = 0.95f,
			},
		});

		float sizeX = (float)terrain.SizeX.ToDouble();
		float sizeZ = (float)terrain.SizeZ.ToDouble();
		AddChild(new MeshInstance3D
		{
			Name = "Water",
			Mesh = new PlaneMesh { Size = new Vector2(sizeX, sizeZ) },
			Position = new Vector3(sizeX / 2, (float)terrain.SeaLevel.ToDouble(), sizeZ / 2),
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.15f, 0.35f, 0.55f, 0.75f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				Roughness = 0.1f,
				Metallic = 0.2f,
			},
		});
	}

	/// <summary>Ground height in world units at a position, for the camera and later for placing objects.</summary>
	public float HeightAt(float x, float z)
	{
		if (_terrain == null)
		{
			return 0;
		}

		var h = _terrain.HeightAt(
			Sim.Core.Fixed.FromRaw((long)(x * (1 << Sim.Core.Fixed.FractionBits))),
			Sim.Core.Fixed.FromRaw((long)(z * (1 << Sim.Core.Fixed.FractionBits))));
		return Mathf.Max((float)h.ToDouble(), (float)_terrain.SeaLevel.ToDouble());
	}

	private static ArrayMesh BuildGroundMesh(Terrain terrain)
	{
		int width = terrain.Width;
		int depth = terrain.Depth;
		float cell = (float)terrain.CellSize.ToDouble();
		float sea = (float)terrain.SeaLevel.ToDouble();

		var heights = new float[width * depth];
		float maxHeight = sea + 1;
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				float h = (float)terrain.HeightAtPoint(x, z).ToDouble();
				heights[z * width + x] = h;
				maxHeight = Mathf.Max(maxHeight, h);
			}
		}

		var vertices = new Vector3[width * depth];
		var normals = new Vector3[width * depth];
		var colors = new Color[width * depth];
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int i = z * width + x;
				float h = heights[i];
				vertices[i] = new Vector3(x * cell, h, z * cell);

				// Central differences give smooth lighting across cells.
				float left = heights[z * width + Mathf.Max(x - 1, 0)];
				float right = heights[z * width + Mathf.Min(x + 1, width - 1)];
				float back = heights[Mathf.Max(z - 1, 0) * width + x];
				float front = heights[Mathf.Min(z + 1, depth - 1) * width + x];
				var normal = new Vector3(left - right, 2 * cell, back - front).Normalized();
				normals[i] = normal;

				colors[i] = ColorFor(h, sea, maxHeight, normal.Y);
			}
		}

		var indices = new int[(width - 1) * (depth - 1) * 6];
		int k = 0;
		for (int z = 0; z < depth - 1; z++)
		{
			for (int x = 0; x < width - 1; x++)
			{
				int a = z * width + x;
				int b = a + 1;
				int c = a + width;
				int d = c + 1;

				// Clockwise when seen from above, which Godot treats as front-facing.
				indices[k++] = a;
				indices[k++] = b;
				indices[k++] = c;
				indices[k++] = b;
				indices[k++] = d;
				indices[k++] = c;
			}
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Normal] = normals;
		arrays[(int)Mesh.ArrayType.Color] = colors;
		arrays[(int)Mesh.ArrayType.Index] = indices;

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	private static Color ColorFor(float height, float sea, float maxHeight, float upness)
	{
		if (height < sea - 1.5f)
		{
			return DeepWater;
		}

		if (height < sea + 1.0f)
		{
			return Sand;
		}

		// Steep slopes show bare rock regardless of height.
		if (upness < 0.8f)
		{
			return Rock;
		}

		float t = (height - sea) / (maxHeight - sea);
		if (t < 0.35f)
		{
			return Grass;
		}

		if (t < 0.6f)
		{
			return Grass.Lerp(Forest, (t - 0.35f) / 0.25f);
		}

		if (t < 0.85f)
		{
			return Forest.Lerp(Rock, (t - 0.6f) / 0.25f);
		}

		return Rock.Lerp(Snow, (t - 0.85f) / 0.15f);
	}
}
