using System;
using Godot;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Game;

/// <summary>
/// Builds a city's buildings at real scale: a street grid of blocks with towers downtown,
/// apartment blocks around them and small houses in the suburbs. Purely visual, so it uses its
/// own random numbers seeded by the city id rather than the simulation's.
/// </summary>
public static class CityBuilder
{
	/// <summary>Distance between street centre lines, in metres.</summary>
	private const float BlockSize = 160f;

	/// <summary>Built-up part of a block; the rest is street.</summary>
	private const float Lot = BlockSize - 24f;

	private static readonly (float U, float V)[] Quarters =
	{
		(-Lot / 4, -Lot / 4), (Lot / 4, -Lot / 4), (-Lot / 4, Lot / 4), (Lot / 4, Lot / 4),
	};

	/// <summary>Buildings are not drawn beyond this camera distance; the city marker takes over.</summary>
	public const float VisibleUpTo = 250_000f;

	private static readonly Color[] Concrete =
	{
		new(0.62f, 0.60f, 0.57f), new(0.70f, 0.67f, 0.60f), new(0.55f, 0.55f, 0.56f),
		new(0.72f, 0.64f, 0.52f), new(0.60f, 0.52f, 0.46f),
	};

	private static readonly Color[] Glass = { new(0.38f, 0.48f, 0.58f), new(0.30f, 0.38f, 0.46f), new(0.50f, 0.56f, 0.62f) };

	private static readonly Color[] Roofs = { new(0.55f, 0.27f, 0.20f), new(0.45f, 0.30f, 0.25f), new(0.40f, 0.40f, 0.42f) };

	/// <summary>Radius of the built-up area in metres; grows with the square root of population.</summary>
	public static float RadiusOf(City city) => 2_500f + Mathf.Sqrt(city.PopulationThousands) * 170f;

	/// <summary>0 outside the city, rising to 1 at its centre. Used to grey the ground under it.</summary>
	public static float UrbanAmount(City city, float x, float z)
	{
		float dx = x - (float)city.X.ToDouble();
		float dz = z - (float)city.Z.ToDouble();
		float r = Mathf.Sqrt(dx * dx + dz * dz) / RadiusOf(city);
		return r >= 1 ? 0 : Mathf.Sqrt(1 - r);
	}

	public static MultiMeshInstance3D Build(City city, TerrainView terrain)
	{
		var random = new Random(city.Id * 7919 + 17);
		float cx = (float)city.X.ToDouble();
		float cz = (float)city.Z.ToDouble();
		float radius = RadiusOf(city);
		float sea = terrain.SeaLevel;

		// Rotating the street grid keeps cities from all looking aligned to the map.
		float angle = (float)random.NextDouble() * Mathf.Pi / 2;
		var along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
		var across = new Vector2(-along.Y, along.X);

		// Small cities have lower downtowns.
		float towerHeight = 40f + Mathf.Sqrt(city.PopulationThousands) * (city.IsCapital ? 4.5f : 2.5f);

		var transforms = new System.Collections.Generic.List<Transform3D>();
		var colors = new System.Collections.Generic.List<Color>();

		int blocks = (int)(radius / BlockSize);
		for (int i = -blocks; i <= blocks; i++)
		{
			for (int j = -blocks; j <= blocks; j++)
			{
				var offset = along * (i * BlockSize) + across * (j * BlockSize);
				float r = offset.Length() / radius;

				// Ragged edge: density falls off toward the outskirts, with some open spaces (parks) inside.
				float density = 1 - Mathf.Pow(r, 1.6f);
				if (density <= 0 || random.NextDouble() > density * 0.95 + 0.02)
				{
					continue;
				}

				float bx = cx + offset.X;
				float bz = cz + offset.Y;
				if (!IsBuildable(terrain, bx, bz, sea))
				{
					continue;
				}

				void Add(float u, float v, float width, float depth, float height, Color color) =>
					AddBuilding(terrain, transforms, colors, random, bx, bz, along, across, u, v, width, depth, height, color);

				if (r < 0.18f)
				{
					// Downtown: a tower on each quarter of the block, tallest in the middle.
					foreach (var (u, v) in Quarters)
					{
						if (random.NextDouble() < 0.85)
						{
							float w = Range(random, 34, 58);
							float h = Mathf.Max(25, towerHeight * Range(random, 0.3f, 1f) * (1 - r * 2.5f));
							Add(u, v, w, w * Range(random, 0.8f, 1.2f), h, PickColor(random, random.NextDouble() < 0.35 ? Glass : Concrete));
						}
					}
				}
				else if (r < 0.5f)
				{
					// Apartment blocks around a courtyard, two buildings per side.
					const float edge = Lot / 2 - 10;
					for (int k = -1; k <= 1; k += 2)
					{
						float h = Range(random, 12, 32);
						var color = PickColor(random, Concrete);
						Add(k * Lot / 4, -edge, Lot / 2 - 4, 18, h, color);
						Add(k * Lot / 4, edge, Lot / 2 - 4, 18, Range(random, 12, 32), PickColor(random, Concrete));
						Add(-edge, k * Lot / 4, 18, Lot / 2 - 26, Range(random, 12, 32), PickColor(random, Concrete));
						Add(edge, k * Lot / 4, 18, Lot / 2 - 26, h, color);
					}
				}
				else if (random.NextDouble() < 0.08)
				{
					// The odd warehouse or factory on the outskirts.
					Add(0, 0, Range(random, 60, 110), Range(random, 40, 80), Range(random, 8, 14), PickColor(random, Concrete));
				}
				else
				{
					// Suburbs: a 3 x 3 grid of houses with gardens.
					for (int u = -1; u <= 1; u++)
					{
						for (int v = -1; v <= 1; v++)
						{
							if (random.NextDouble() < 0.8)
							{
								Add(u * 45 + Range(random, -5, 5), v * 45 + Range(random, -5, 5), Range(random, 10, 16), Range(random, 9, 13),
									Range(random, 5, 8), PickColor(random, random.NextDouble() < 0.6 ? Roofs : Concrete));
							}
						}
					}
				}
			}
		}

		var multimesh = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors = true,
			Mesh = new BoxMesh { Size = Vector3.One },
			InstanceCount = transforms.Count,
		};
		for (int k = 0; k < transforms.Count; k++)
		{
			multimesh.SetInstanceTransform(k, transforms[k]);
			multimesh.SetInstanceColor(k, colors[k]);
		}

		return new MultiMeshInstance3D
		{
			Name = "Buildings",
			Multimesh = multimesh,
			MaterialOverride = new StandardMaterial3D
			{
				VertexColorUseAsAlbedo = true,
				VertexColorIsSrgb = true,
				Roughness = 0.85f,
			},
			VisibilityRangeEnd = VisibleUpTo,
			VisibilityRangeEndMargin = VisibleUpTo * 0.1f,
			VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
		};
	}

	/// <summary>Dry, fairly flat ground.</summary>
	public static bool IsBuildable(TerrainView terrain, float x, float z, float sea)
	{
		const float probe = 60f;
		float h = terrain.HeightAt(x, z);
		if (h < sea + 1.5f)
		{
			return false;
		}

		float slope = Mathf.Max(Mathf.Abs(terrain.HeightAt(x + probe, z) - h), Mathf.Abs(terrain.HeightAt(x, z + probe) - h));
		return slope < 12f;
	}

	private static void AddBuilding(
		TerrainView terrain, System.Collections.Generic.List<Transform3D> transforms, System.Collections.Generic.List<Color> colors,
		Random random, float bx, float bz, Vector2 along, Vector2 across, float u, float v, float width, float depth, float height, Color color)
	{
		var offset = along * u + across * v;
		float x = bx + offset.X;
		float z = bz + offset.Y;
		float ground = terrain.HeightAt(x, z);
		if (ground < terrain.SeaLevel + 1.5f)
		{
			return;
		}

		// Sink a few metres so buildings on slopes do not float.
		const float sink = 3f;
		var basis = new Basis(Vector3.Up, -Mathf.Atan2(along.Y, along.X)).Scaled(new Vector3(width, height + sink, depth));
		transforms.Add(new Transform3D(basis, new Vector3(x, ground - sink + (height + sink) / 2, z)));
		colors.Add(color * (0.9f + 0.2f * (float)random.NextDouble()));
	}

	private static float Range(Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

	private static Color PickColor(Random random, Color[] palette) => palette[random.Next(palette.Length)];
}
