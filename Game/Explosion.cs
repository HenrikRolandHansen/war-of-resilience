using Godot;

namespace WarOfResilience.Game;

/// <summary>A short fireball and flash, sized so it stays visible from far away. Frees itself.</summary>
public partial class Explosion : Node3D
{
	private const float Duration = 1.2f;

	private readonly float _size;
	private float _age;
	private MeshInstance3D _ball = null!;
	private StandardMaterial3D _material = null!;
	private OmniLight3D _light = null!;

	public Explosion() : this(60f)
	{
	}

	public Explosion(float size)
	{
		_size = size;
	}

	public override void _Ready()
	{
		_material = new StandardMaterial3D
		{
			AlbedoColor = new Color(1f, 0.55f, 0.15f, 1f),
			EmissionEnabled = true,
			Emission = new Color(1f, 0.45f, 0.1f),
			EmissionEnergyMultiplier = 3f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		_ball = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.5f, Height = 1f }, MaterialOverride = _material };
		AddChild(_ball);
		_light = new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.3f), OmniRange = _size * 6, LightEnergy = 8 };
		AddChild(_light);
		Update(0);
	}

	public override void _Process(double delta)
	{
		_age += (float)delta;
		if (_age >= Duration)
		{
			QueueFree();
			return;
		}

		Update(_age / Duration);
	}

	private void Update(float t)
	{
		float scale = _size * (0.3f + 1.7f * Mathf.Sqrt(t));
		_ball.Scale = new Vector3(scale, scale, scale);
		var color = _material.AlbedoColor;
		color.A = 1 - t;
		_material.AlbedoColor = color;
		_light.LightEnergy = 8 * (1 - t);
	}
}
