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

	/// <summary>Grid cells per side of one ground mesh chunk.</summary>
	private const int ChunkCells = 100;

	private Terrain? _terrain;

	public void Build(Terrain terrain)
	{
		_terrain = terrain;

		foreach (var child in GetChildren())
		{
			child.QueueFree();
		}

		var groundMaterial = new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true,
			VertexColorIsSrgb = true,
			Roughness = 0.95f,
		};
		BuildGround(terrain, groundMaterial);

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

	/// <summary>
	/// Splits the ground into square chunks so Godot can skip the ones outside the camera's view.
	/// Normals and colours come from the whole height grid, so chunk edges line up seamlessly.
	/// </summary>
	private void BuildGround(Terrain terrain, Material material)
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

		var ground = new Node3D { Name = "Ground" };
		AddChild(ground);

		for (int chunkZ = 0; chunkZ < depth - 1; chunkZ += ChunkCells)
		{
			for (int chunkX = 0; chunkX < width - 1; chunkX += ChunkCells)
			{
				int cellsX = Mathf.Min(ChunkCells, width - 1 - chunkX);
				int cellsZ = Mathf.Min(ChunkCells, depth - 1 - chunkZ);
				ground.AddChild(new MeshInstance3D
				{
					Name = $"Chunk_{chunkX / ChunkCells}_{chunkZ / ChunkCells}",
					Mesh = BuildChunkMesh(heights, width, depth, cell, sea, maxHeight, chunkX, chunkZ, cellsX, cellsZ),
					MaterialOverride = material,
				});
			}
		}
	}

	private static ArrayMesh BuildChunkMesh(
		float[] heights, int width, int depth, float cell, float sea, float maxHeight,
		int startX, int startZ, int cellsX, int cellsZ)
	{
		int pointsX = cellsX + 1;
		int pointsZ = cellsZ + 1;
		var vertices = new Vector3[pointsX * pointsZ];
		var normals = new Vector3[pointsX * pointsZ];
		var colors = new Color[pointsX * pointsZ];

		for (int lz = 0; lz < pointsZ; lz++)
		{
			for (int lx = 0; lx < pointsX; lx++)
			{
				int x = startX + lx;
				int z = startZ + lz;
				int i = lz * pointsX + lx;
				float h = heights[z * width + x];
				vertices[i] = new Vector3(x * cell, h, z * cell);

				// Central differences give smooth lighting across cells and chunks.
				float left = heights[z * width + Mathf.Max(x - 1, 0)];
				float right = heights[z * width + Mathf.Min(x + 1, width - 1)];
				float back = heights[Mathf.Max(z - 1, 0) * width + x];
				float front = heights[Mathf.Min(z + 1, depth - 1) * width + x];
				var normal = new Vector3(left - right, 2 * cell, back - front).Normalized();
				normals[i] = normal;

				colors[i] = ColorFor(h, sea, maxHeight, normal.Y);
			}
		}

		var indices = new int[cellsX * cellsZ * 6];
		int k = 0;
		for (int lz = 0; lz < cellsZ; lz++)
		{
			for (int lx = 0; lx < cellsX; lx++)
			{
				int a = lz * pointsX + lx;
				int b = a + 1;
				int c = a + pointsX;
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
