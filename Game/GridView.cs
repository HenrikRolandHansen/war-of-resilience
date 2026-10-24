using System.Collections.Generic;
using Godot;
using WarOfResilience.Sim.Grid;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Game;

/// <summary>
/// Draws the power grid: real-scale power plants, transformer stations, pylons and wires up close,
/// and simple markers when zoomed out. Lines glow while they carry power and turn grey when cut
/// off. Only reads from Sim.
/// </summary>
public partial class GridView : Node3D
{
	/// <summary>Models and pylons are hidden beyond this camera distance; markers show instead.</summary>
	private const float ModelsVisibleUpTo = 120_000f;

	/// <summary>Markers are hidden closer than this, where the models are clear enough.</summary>
	private const float MarkersHiddenBelow = 40_000f;

	private const string MarkerName = "Marker";

	private const float PylonSpacing = 400f;
	private const float PylonHeight = 38f;
	private const float WireLift = PylonHeight - 2f;

	private static readonly Color Energized = new(1f, 0.82f, 0.25f);
	private static readonly Color Dead = new(0.3f, 0.3f, 0.3f);
	private static readonly Color Cut = new(0.55f, 0.08f, 0.05f);
	private static readonly StandardMaterial3D Charred = new() { AlbedoColor = new Color(0.08f, 0.07f, 0.06f), Roughness = 1f };

	private readonly List<(PowerLine Line, StandardMaterial3D Material)> _lines = new();
	private readonly List<Marker> _markers = new();
	private readonly List<Node3D> _rotors = new();
	private readonly List<bool> _lineState = new();
	private PowerGrid? _grid;
	private RtsCamera? _camera;
	private TerrainView? _terrain;

	public void Build(PowerGrid grid, TerrainView terrain, RtsCamera camera)
	{
		_camera = camera;
		_terrain = terrain;
		_grid = grid;

		foreach (var line in grid.Lines)
		{
			var from = grid.Nodes[line.FromNodeId];
			var to = grid.Nodes[line.ToNodeId];
			var a = new Vector2((float)from.X.ToDouble(), (float)from.Z.ToDouble());
			var b = new Vector2((float)to.X.ToDouble(), (float)to.Z.ToDouble());
			var material = new StandardMaterial3D
			{
				AlbedoColor = line.IsEnergized ? Energized : Dead,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			};
			AddChild(new MeshInstance3D { Name = $"Line_{line.Id}", Mesh = BuildWire(a, b), MaterialOverride = material });
			_lines.Add((line, material));
			_lineState.Add(line.IsOperational);
			AddPylons(a, b);
		}

		foreach (var node in grid.Nodes)
		{
			float x = (float)node.X.ToDouble();
			float z = (float)node.Z.ToDouble();
			var root = new Node3D
			{
				Name = node is PowerPlant p ? $"{p.Kind}Plant_{node.Id}" : $"Substation_{node.Id}",
				Position = new Vector3(x, terrain.HeightAt(x, z), z),
			};
			AddChild(root);

			var color = WorldView.ColorOf(node.CountryId);
			if (node is PowerPlant plant)
			{
				switch (plant.Kind)
				{
					case PowerPlantKind.Coal:
						BuildCoalPlant(root);
						break;
					case PowerPlantKind.Gas:
						BuildGasPlant(root);
						break;
					case PowerPlantKind.Wind:
						BuildWindFarm(root, x, z);
						break;
				}

				var label = new Label3D
				{
					Text = $"{plant.Kind} {plant.CapacityMW:N0} MW",
					Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
					FixedSize = true,
					PixelSize = 0.0018f,
					FontSize = 24,
					OutlineSize = 6,
					Modulate = new Color(1f, 0.92f, 0.6f),
					OutlineModulate = new Color(0, 0, 0, 0.8f),
					NoDepthTest = true,
					VerticalAlignment = VerticalAlignment.Bottom,
					VisibilityRangeEnd = 450_000f,
				};
				root.AddChild(label);
				_markers.Add(new Marker(node, root, AddMarker(root, color, diamond: true), label, 0.016f));
			}
			else
			{
				BuildSubstation(root);
				_markers.Add(new Marker(node, root, AddMarker(root, color.Lightened(0.4f), diamond: false), null, 0.006f));
			}
		}

		UpdateView(0);
	}

	public override void _Process(double delta) => UpdateView((float)delta);

	private void UpdateView(float delta)
	{
		for (int i = 0; i < _lines.Count; i++)
		{
			var (line, material) = _lines[i];
			material.AlbedoColor = !line.IsOperational ? Cut : line.IsEnergized ? Energized : Dead;
			if (_lineState[i] && !line.IsOperational)
			{
				_lineState[i] = false;
				OnLineCut(line);
			}
		}

		foreach (var marker in _markers)
		{
			if (marker.Node.HitPoints != marker.ShownHitPoints)
			{
				OnDamaged(marker);
			}
		}

		if (_camera == null)
		{
			return;
		}

		float distance = _camera.Distance;
		bool showMarkers = distance >= MarkersHiddenBelow;
		foreach (var marker in _markers)
		{
			float size = distance * marker.Scale;
			marker.Mesh.Visible = showMarkers;
			marker.Mesh.Scale = new Vector3(size, size, size);
			marker.Mesh.Position = new Vector3(0, size, 0);
			if (marker.Label != null)
			{
				marker.Label.Position = new Vector3(0, showMarkers ? size * 2.2f : 260f, 0);
			}
		}

		if (distance < ModelsVisibleUpTo)
		{
			foreach (var rotor in _rotors)
			{
				rotor.RotateObjectLocal(Vector3.Forward, delta * 1.6f);
			}
		}
	}

	/// <summary>Wire following the ground at pylon-top height, sampled every 500 m.</summary>
	private ImmediateMesh BuildWire(Vector2 a, Vector2 b)
	{
		var mesh = new ImmediateMesh();
		mesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
		int steps = Mathf.Max(1, (int)(a.DistanceTo(b) / 500f));
		for (int i = 0; i <= steps; i++)
		{
			var p = a.Lerp(b, (float)i / steps);
			mesh.SurfaceAddVertex(new Vector3(p.X, _terrain!.HeightAt(p.X, p.Y) + WireLift, p.Y));
		}

		mesh.SurfaceEnd();
		return mesh;
	}

	/// <summary>
	/// Pylons along a line, in groups of <see cref="PylonsPerGroup"/>: visibility range is measured
	/// to each group's centre, so one group spanning a whole line would vanish when zoomed in on its end.
	/// </summary>
	private void AddPylons(Vector2 a, Vector2 b)
	{
		var direction = (b - a).Normalized();
		float heading = -Mathf.Atan2(direction.Y, direction.X);
		int count = Mathf.Max(1, (int)(a.DistanceTo(b) / PylonSpacing));
		var group = new List<Transform3D>();
		for (int i = 1; i < count; i++)
		{
			var p = a.Lerp(b, (float)i / count);
			float ground = _terrain!.HeightAt(p.X, p.Y);
			group.Add(new Transform3D(new Basis(Vector3.Up, heading), new Vector3(p.X, ground, p.Y)));
			if (group.Count == PylonsPerGroup || i == count - 1)
			{
				AddPylonMeshes(group);
				group.Clear();
			}
		}
	}

	private const int PylonsPerGroup = 25;

	private static readonly StandardMaterial3D Steel = new() { AlbedoColor = new Color(0.55f, 0.56f, 0.58f), Roughness = 0.6f, Metallic = 0.3f };
	private static readonly Mesh PylonMast = new CylinderMesh { TopRadius = 0.8f, BottomRadius = 4f, Height = PylonHeight, RadialSegments = 4, Rings = 1 };
	private static readonly Mesh PylonArm = new BoxMesh { Size = new Vector3(1.2f, 1.5f, 22f) };

	/// <summary>Lattice-style pylons from two parts: a tapering mast and a cross-arm across the line.</summary>
	private void AddPylonMeshes(List<Transform3D> transforms)
	{
		foreach (var (mesh, offset) in new (Mesh, float)[] { (PylonMast, PylonHeight / 2), (PylonArm, PylonHeight - 3) })
		{
			var multimesh = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				Mesh = mesh,
				InstanceCount = transforms.Count,
			};
			for (int i = 0; i < transforms.Count; i++)
			{
				multimesh.SetInstanceTransform(i, transforms[i].Translated(new Vector3(0, offset, 0)));
			}

			AddChild(new MultiMeshInstance3D
			{
				Name = "Pylons",
				Multimesh = multimesh,
				MaterialOverride = Steel,
				VisibilityRangeEnd = ModelsVisibleUpTo,
			});
		}
	}

	private static MeshInstance3D AddMarker(Node3D root, Color color, bool diamond)
	{
		var mesh = new MeshInstance3D
		{
			Name = MarkerName,
			Mesh = new BoxMesh { Size = Vector3.One },
			Rotation = diamond ? new Vector3(Mathf.Pi / 4, 0, Mathf.Pi / 4) : Vector3.Zero,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = diamond ? new Color(1f, 0.85f, 0.3f) : color,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			},
		};
		root.AddChild(mesh);
		return mesh;
	}

	private static void BuildCoalPlant(Node3D root)
	{
		var concrete = Material(new Color(0.62f, 0.6f, 0.56f));
		var brick = Material(new Color(0.5f, 0.32f, 0.26f));
		var coal = Material(new Color(0.12f, 0.12f, 0.12f));

		AddPart(root, new BoxMesh { Size = new Vector3(360, 6, 260) }, Material(new Color(0.4f, 0.4f, 0.38f)), new Vector3(0, 0, 0));
		AddPart(root, new BoxMesh { Size = new Vector3(160, 55, 70) }, brick, new Vector3(-40, 27, -30));
		AddPart(root, new BoxMesh { Size = new Vector3(60, 80, 50) }, brick, new Vector3(60, 40, -30));
		AddPart(root, new CylinderMesh { TopRadius = 5, BottomRadius = 8, Height = 200 }, concrete, new Vector3(110, 100, -70));
		for (int i = 0; i < 2; i++)
		{
			// Hyperboloid-ish cooling towers.
			AddPart(root, new CylinderMesh { TopRadius = 32, BottomRadius = 48, Height = 130, RadialSegments = 24 }, concrete,
				new Vector3(-110 + i * 110, 65, 70));
		}

		AddPart(root, new BoxMesh { Size = new Vector3(90, 14, 50) }, coal, new Vector3(-130, 7, -90));
	}

	private static void BuildGasPlant(Node3D root)
	{
		var metal = Material(new Color(0.75f, 0.76f, 0.78f));
		var hall = Material(new Color(0.55f, 0.62f, 0.66f));

		AddPart(root, new BoxMesh { Size = new Vector3(240, 6, 180) }, Material(new Color(0.4f, 0.4f, 0.38f)), Vector3.Zero);
		AddPart(root, new BoxMesh { Size = new Vector3(120, 35, 50) }, hall, new Vector3(-20, 17, -20));
		for (int i = 0; i < 2; i++)
		{
			AddPart(root, new CylinderMesh { TopRadius = 4, BottomRadius = 4, Height = 75 }, metal, new Vector3(50, 37, -40 + i * 40));
		}

		for (int i = 0; i < 3; i++)
		{
			AddPart(root, new CylinderMesh { TopRadius = 14, BottomRadius = 14, Height = 18 }, metal, new Vector3(-80 + i * 35, 9, 55));
		}
	}

	/// <summary>A few dozen turbines in rows across the hills around the farm's centre.</summary>
	private void BuildWindFarm(Node3D root, float cx, float cz)
	{
		var white = Material(new Color(0.9f, 0.91f, 0.92f));
		var tower = new CylinderMesh { TopRadius = 1.6f, BottomRadius = 2.8f, Height = 100, RadialSegments = 12 };
		var nacelle = new BoxMesh { Size = new Vector3(4, 4, 12) };
		var blade = new BoxMesh { Size = new Vector3(2.5f, 55, 0.8f) };

		for (int row = -2; row <= 2; row++)
		{
			for (int col = -3; col <= 3; col++)
			{
				float x = col * 600 + (row % 2) * 300;
				float z = row * 700;
				float ground = _terrain!.HeightAt(cx + x, cz + z);
				if (ground <= _terrain.SeaLevel + 1)
				{
					continue;
				}

				var turbine = new Node3D { Position = new Vector3(x, ground - root.Position.Y, z) };
				root.AddChild(turbine);
				AddPart(turbine, tower, white, new Vector3(0, 50, 0));
				AddPart(turbine, nacelle, white, new Vector3(0, 101, 0));

				// Rotor faces south-east, toward the default camera.
				var rotor = new Node3D { Position = new Vector3(0, 101, 6.5f) };
				turbine.AddChild(rotor);
				for (int i = 0; i < 3; i++)
				{
					var arm = new Node3D { Rotation = new Vector3(0, 0, i * Mathf.Tau / 3) };
					rotor.AddChild(arm);
					AddPart(arm, blade, white, new Vector3(0, 27.5f, 0));
				}

				rotor.Rotation = new Vector3(0, 0, (col * 0.7f + row) % Mathf.Tau);
				_rotors.Add(rotor);
			}
		}
	}

	/// <summary>Transformer station: fenced gravel yard with transformers and steel gantries.</summary>
	private static void BuildSubstation(Node3D root)
	{
		var gravel = Material(new Color(0.5f, 0.49f, 0.46f));
		var transformer = Material(new Color(0.42f, 0.47f, 0.42f));
		var steel = Material(new Color(0.7f, 0.71f, 0.73f));
		var fence = Material(new Color(0.35f, 0.36f, 0.37f));

		AddPart(root, new BoxMesh { Size = new Vector3(180, 2, 130) }, gravel, new Vector3(0, 0.5f, 0));
		foreach (var (size, position) in new[]
		{
			(new Vector3(180, 3, 0.5f), new Vector3(0, 1.5f, -65)), (new Vector3(180, 3, 0.5f), new Vector3(0, 1.5f, 65)),
			(new Vector3(0.5f, 3, 130), new Vector3(-90, 1.5f, 0)), (new Vector3(0.5f, 3, 130), new Vector3(90, 1.5f, 0)),
		})
		{
			AddPart(root, new BoxMesh { Size = size }, fence, position);
		}

		for (int i = 0; i < 4; i++)
		{
			AddPart(root, new BoxMesh { Size = new Vector3(10, 9, 8) }, transformer, new Vector3(-60 + i * 40, 4.5f, 25));
		}

		for (int i = 0; i < 3; i++)
		{
			// Gantries: two posts and a beam.
			float x = -50 + i * 50;
			AddPart(root, new BoxMesh { Size = new Vector3(1, 18, 1) }, steel, new Vector3(x, 9, -40));
			AddPart(root, new BoxMesh { Size = new Vector3(1, 18, 1) }, steel, new Vector3(x, 9, -10));
			AddPart(root, new BoxMesh { Size = new Vector3(1, 1, 31) }, steel, new Vector3(x, 18, -25));
		}

		AddPart(root, new BoxMesh { Size = new Vector3(25, 6, 14) }, Material(new Color(0.75f, 0.72f, 0.65f)), new Vector3(70, 3, -45));
	}

	private static void AddPart(Node3D parent, Mesh mesh, Material material, Vector3 position)
	{
		parent.AddChild(new MeshInstance3D
		{
			Mesh = mesh,
			MaterialOverride = material,
			Position = position,
			VisibilityRangeEnd = ModelsVisibleUpTo,
		});
	}

	private static StandardMaterial3D Material(Color color) => new() { AlbedoColor = color, Roughness = 0.8f };

	/// <summary>Explosion, then charred models, smoke and an updated label.</summary>
	private void OnDamaged(Marker marker)
	{
		var node = marker.Node;
		marker.ShownHitPoints = node.HitPoints;
		float size = Mathf.Max(80f, (_camera?.Distance ?? 0) * 0.02f);
		marker.Root.AddChild(new Explosion(size) { Position = new Vector3(0, 30, 0) });

		if (marker.Label != null && node is PowerPlant plant)
		{
			marker.Label.Text = node.IsOperational
				? $"{plant.Kind} {plant.CapacityMW:N0} MW ({node.HitPoints * 100 / node.MaxHitPoints}%)"
				: $"{plant.Kind} plant DESTROYED";
			marker.Label.Modulate = node.IsOperational ? new Color(1f, 0.7f, 0.4f) : new Color(1f, 0.35f, 0.3f);
		}

		if (!node.IsOperational)
		{
			Char(marker.Root);
			marker.Mesh.MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.2f, 0.2f, 0.2f),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			};
			marker.Root.AddChild(BuildSmoke(node is PowerPlant ? 120f : 40f));
		}
	}

	/// <summary>A pylon comes down: explosion where it was hit.</summary>
	private void OnLineCut(PowerLine line)
	{
		var from = _grid!.Nodes[line.FromNodeId];
		var to = _grid.Nodes[line.ToNodeId];

		// The sim does not keep where the line was hit, so show it at the middle.
		float x = (float)((from.X + to.X).ToDouble() / 2);
		float z = (float)((from.Z + to.Z).ToDouble() / 2);
		AddChild(new Explosion(Mathf.Max(60f, (_camera?.Distance ?? 0) * 0.02f))
		{
			Position = new Vector3(x, _terrain!.HeightAt(x, z) + 30, z),
		});
	}

	/// <summary>Turns every model part under the node black, leaving the marker, labels and effects alone.</summary>
	private static void Char(Node node)
	{
		foreach (var child in node.GetChildren())
		{
			if (child is Explosion or Label3D || child.Name == MarkerName)
			{
				continue;
			}

			if (child is MeshInstance3D mesh)
			{
				mesh.MaterialOverride = Charred;
			}

			Char(child);
		}
	}

	private static MeshInstance3D BuildSmoke(float radius) => new()
	{
		Name = "Smoke",
		Mesh = new CylinderMesh { TopRadius = radius * 1.6f, BottomRadius = radius * 0.5f, Height = radius * 6 },
		Position = new Vector3(0, radius * 3, 0),
		MaterialOverride = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.15f, 0.15f, 0.15f, 0.45f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		},
		VisibilityRangeEnd = ModelsVisibleUpTo * 2,
	};

	private sealed class Marker
	{
		public Marker(GridNode node, Node3D root, MeshInstance3D mesh, Label3D? label, float scale)
		{
			Node = node;
			Root = root;
			Mesh = mesh;
			Label = label;
			Scale = scale;
			ShownHitPoints = node.HitPoints;
		}

		public GridNode Node { get; }

		public Node3D Root { get; }

		public MeshInstance3D Mesh { get; }

		public Label3D? Label { get; }

		public float Scale { get; }

		public int ShownHitPoints { get; set; }
	}
}
