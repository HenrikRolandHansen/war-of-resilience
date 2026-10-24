using System;
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
	private static readonly Color Urban = new(0.42f, 0.42f, 0.40f);

	/// <summary>How strongly a country's colour tints its land (0 = none, 1 = solid).</summary>
	private const float TerritoryTint = 0.12f;

	/// <summary>Grid cells per side of one ground mesh chunk.</summary>
	private const int ChunkCells = 100;

	private Terrain? _terrain;
	private ShaderMaterial? _groundMaterial;
	private ShaderMaterial? _waterMaterial;

	/// <param name="ownerAtPoint">Optional country owner per grid point (-1 for none); owned land is tinted in the country's colour.</param>
	/// <param name="urbanAtPoint">Optional 0..1 per grid point; built-up land is greyed toward asphalt.</param>
	public void Build(Terrain terrain, Func<int, int, int>? ownerAtPoint = null, Func<int, int, float>? urbanAtPoint = null)
	{
		_terrain = terrain;

		foreach (var child in GetChildren())
		{
			child.QueueFree();
		}

		float sizeX = (float)terrain.SizeX.ToDouble();
		float sizeZ = (float)terrain.SizeZ.ToDouble();

		_groundMaterial = new ShaderMaterial { Shader = new Shader { Code = GroundShader } };
		_waterMaterial = new ShaderMaterial { Shader = new Shader { Code = WaterShader } };
		_waterMaterial.SetShaderParameter("water_color", new Color(0.15f, 0.35f, 0.55f, 0.75f));
		foreach (var material in new[] { _groundMaterial, _waterMaterial })
		{
			material.SetShaderParameter("map_size", new Vector2(sizeX, sizeZ));
			material.SetShaderParameter("fog", PlaceholderFog());
		}

		BuildGround(terrain, _groundMaterial, ownerAtPoint, urbanAtPoint);

		AddChild(new MeshInstance3D
		{
			Name = "Water",
			Mesh = new PlaneMesh { Size = new Vector2(sizeX, sizeZ) },
			Position = new Vector3(sizeX / 2, (float)terrain.SeaLevel.ToDouble(), sizeZ / 2),
			MaterialOverride = _waterMaterial,
		});
	}

	/// <summary>Darkens ground and water the local player cannot see (see <see cref="FogOfWar"/>).</summary>
	public void SetFog(Texture2D fog)
	{
		_groundMaterial?.SetShaderParameter("fog", fog);
		_waterMaterial?.SetShaderParameter("fog", fog);
	}

	/// <summary>All-visible fog until the real one is set.</summary>
	private static Texture2D PlaceholderFog()
	{
		var image = Image.CreateEmpty(1, 1, false, Image.Format.L8);
		image.Fill(Colors.White);
		return ImageTexture.CreateFromImage(image);
	}

	private const string FogFunctions = @"
uniform sampler2D fog : filter_linear, repeat_disable;
uniform vec2 map_size;
varying vec2 world_xz;

// Never seen: dark. Seen before: dim. In sight now: full brightness.
float fog_light() {
	float f = texture(fog, world_xz / map_size).r;
	return mix(0.45, 1.0, f);
}
";

	private const string GroundShader = @"
shader_type spatial;
" + FogFunctions + @"
void vertex() {
	world_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

void fragment() {
	// Vertex colours are sRGB; light in linear space.
	vec3 c = COLOR.rgb;
	c = mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
	ALBEDO = c * fog_light();
	ROUGHNESS = 0.95;
}
";

	private const string WaterShader = @"
shader_type spatial;
render_mode blend_mix;
uniform vec4 water_color : source_color;
" + FogFunctions + @"
void vertex() {
	world_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

void fragment() {
	ALBEDO = water_color.rgb * fog_light();
	ALPHA = water_color.a;
	ROUGHNESS = 0.1;
	METALLIC = 0.2;
}
";

	/// <summary>Height of the water surface in metres.</summary>
	public float SeaLevel => _terrain == null ? 0 : (float)_terrain.SeaLevel.ToDouble();

	/// <summary>Map size along X in metres.</summary>
	public float SizeX => _terrain == null ? 0 : (float)_terrain.SizeX.ToDouble();

	/// <summary>Map size along Z in metres.</summary>
	public float SizeZ => _terrain == null ? 0 : (float)_terrain.SizeZ.ToDouble();

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
	private void BuildGround(Terrain terrain, Material material, Func<int, int, int>? ownerAtPoint, Func<int, int, float>? urbanAtPoint)
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
					Mesh = BuildChunkMesh(heights, width, depth, cell, sea, maxHeight, chunkX, chunkZ, cellsX, cellsZ, ownerAtPoint, urbanAtPoint),
					MaterialOverride = material,
				});
			}
		}
	}

	private static ArrayMesh BuildChunkMesh(
		float[] heights, int width, int depth, float cell, float sea, float maxHeight,
		int startX, int startZ, int cellsX, int cellsZ, Func<int, int, int>? ownerAtPoint, Func<int, int, float>? urbanAtPoint)
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

				var color = ColorFor(h, sea, maxHeight, normal.Y);
				int owner = ownerAtPoint?.Invoke(x, z) ?? -1;
				if (owner >= 0)
				{
					color = color.Lerp(WorldView.ColorOf(owner), TerritoryTint);
				}

				float urban = h > sea + 1.0f ? urbanAtPoint?.Invoke(x, z) ?? 0 : 0;
				if (urban > 0)
				{
					color = color.Lerp(Urban, urban * 0.75f);
				}

				colors[i] = color;
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
