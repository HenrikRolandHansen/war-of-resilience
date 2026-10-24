using System.Collections.Generic;
using Godot;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Game;

/// <summary>
/// Draws cities and the border. Markers grow with the camera distance so they stay readable
/// from street level to the whole 1200 km map. Only reads from Sim.
/// </summary>
public partial class WorldView : Node3D
{
	/// <summary>Marker height as a fraction of the camera distance.</summary>
	private const float MarkerScale = 0.03f;

	private static readonly Color[] CountryColors =
	{
		new(0.25f, 0.55f, 1.0f),
		new(1.0f, 0.32f, 0.25f),
	};

	private readonly List<CityMarker> _markers = new();
	private RtsCamera? _camera;

	public static Color ColorOf(int countryId) => CountryColors[countryId % CountryColors.Length];

	public void Build(WorldState world, TerrainView terrain, RtsCamera camera)
	{
		_camera = camera;

		foreach (var child in GetChildren())
		{
			child.QueueFree();
		}

		_markers.Clear();

		foreach (var city in world.Cities)
		{
			float x = (float)city.X.ToDouble();
			float z = (float)city.Z.ToDouble();
			var root = new Node3D
			{
				Name = city.Name.Replace(' ', '_'),
				Position = new Vector3(x, terrain.HeightAt(x, z), z),
			};
			AddChild(root);

			var color = ColorOf(city.CountryId);
			var pillar = new MeshInstance3D
			{
				Mesh = new CylinderMesh { TopRadius = 0.8f, BottomRadius = 0.8f, Height = 1f },
				MaterialOverride = new StandardMaterial3D
				{
					AlbedoColor = color,
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				},
			};
			root.AddChild(pillar);

			var label = new Label3D
			{
				Text = city.IsCapital ? $"{city.Name.ToUpperInvariant()} (capital)" : city.Name,
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				FixedSize = true,
				PixelSize = 0.0022f,
				FontSize = 28,
				OutlineSize = 8,
				Modulate = Colors.White,
				OutlineModulate = new Color(0, 0, 0, 0.8f),
				NoDepthTest = true,
				VerticalAlignment = VerticalAlignment.Bottom,
			};
			root.AddChild(label);

			_markers.Add(new CityMarker(pillar, label, city.IsCapital ? 1.6f : 1f));
		}

		AddChild(BuildBorder(world, terrain));
		UpdateMarkerSizes();
	}

	public override void _Process(double delta)
	{
		UpdateMarkerSizes();
	}

	private void UpdateMarkerSizes()
	{
		if (_camera == null)
		{
			return;
		}

		float baseHeight = _camera.Distance * MarkerScale;
		foreach (var marker in _markers)
		{
			float height = baseHeight * marker.SizeFactor;
			marker.Pillar.Scale = new Vector3(height, height, height);
			marker.Pillar.Position = new Vector3(0, height / 2, 0);
			marker.Label.Position = new Vector3(0, height * 1.15f, 0);
		}
	}

	private static MeshInstance3D BuildBorder(WorldState world, TerrainView terrain)
	{
		float x = (float)world.BorderX.ToDouble();
		float length = terrain.SizeZ;
		const float step = 2000f;
		const float lift = 30f;

		var mesh = new ImmediateMesh();
		mesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
		for (float z = 0; z <= length; z += step)
		{
			mesh.SurfaceAddVertex(new Vector3(x, terrain.HeightAt(x, z) + lift, z));
		}

		mesh.SurfaceEnd();

		return new MeshInstance3D
		{
			Name = "Border",
			Mesh = mesh,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(1f, 0.95f, 0.6f),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				NoDepthTest = true,
			},
		};
	}

	private readonly record struct CityMarker(MeshInstance3D Pillar, Label3D Label, float SizeFactor);
}
