using System;
using Godot;

namespace WarOfResilience.Game;

/// <summary>
/// Classic angled strategy camera (Red Alert style): fixed pitch and heading, no rotation.
/// Pan with WASD/arrow keys, the screen edge or a middle-mouse drag; zoom with the mouse wheel.
/// The node itself is the point on the ground the camera looks at.
/// </summary>
public partial class RtsCamera : Node3D
{
	[Export] public float PitchDegrees { get; set; } = 55f;
	[Export] public float MinDistance { get; set; } = 200f;
	[Export] public float MaxDistance { get; set; } = 1_500_000f;
	[Export] public float StartDistance { get; set; } = 300_000f;

	/// <summary>Pan speed in metres per second at a distance of 100 m; scales with zoom.</summary>
	[Export] public float PanSpeed { get; set; } = 120f;

	/// <summary>Fraction of the current distance each wheel notch zooms.</summary>
	[Export] public float ZoomStep { get; set; } = 0.2f;

	/// <summary>How quickly the camera catches up with the target zoom and height.</summary>
	[Export] public float Smoothing { get; set; } = 10f;

	/// <summary>Pixels from the window edge that start edge scrolling. 0 turns it off.</summary>
	[Export] public int EdgeScrollMargin { get; set; } = 12;

	private Camera3D _camera = null!;
	private Func<float, float, float> _groundHeight = (_, _) => 0f;
	private Rect2 _bounds = new(0, 0, 1000, 1000);
	private float _distance;
	private float _targetDistance;
	private bool _dragging;

	public override void _Ready()
	{
		_camera = GetNodeOrNull<Camera3D>("Camera3D") ?? CreateCamera();
		_camera.Current = true;
		_distance = _targetDistance = Mathf.Clamp(StartDistance, MinDistance, MaxDistance);
		UpdateCameraTransform();
	}

	/// <summary>Limits panning to the given area (X/Z in metres) and follows the ground height.</summary>
	public void Configure(Rect2 bounds, Func<float, float, float> groundHeight)
	{
		_bounds = bounds;
		_groundHeight = groundHeight;
		var center = bounds.GetCenter();
		Position = new Vector3(center.X, groundHeight(center.X, center.Y), center.Y);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
				_targetDistance = Mathf.Max(MinDistance, _targetDistance * (1 - ZoomStep));
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
				_targetDistance = Mathf.Min(MaxDistance, _targetDistance * (1 + ZoomStep));
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Middle } button:
				_dragging = button.Pressed;
				break;
			case InputEventMouseMotion motion when _dragging:
				// Move the ground under the cursor along with the mouse.
				float metresPerPixel = 2 * _distance * Mathf.Tan(Mathf.DegToRad(_camera.Fov / 2)) / GetViewport().GetVisibleRect().Size.Y;
				Pan(new Vector2(-motion.Relative.X, -motion.Relative.Y) * metresPerPixel);
				break;
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		var direction = KeyboardDirection() + EdgeScrollDirection();
		if (direction != Vector2.Zero)
		{
			Pan(direction.Normalized() * PanSpeed * (_distance / 100f) * dt);
		}

		float blend = 1 - Mathf.Exp(-Smoothing * dt);
		_distance = Mathf.Lerp(_distance, _targetDistance, blend);

		var position = Position;
		position.Y = Mathf.Lerp(position.Y, _groundHeight(position.X, position.Z), blend);
		Position = position;

		UpdateCameraTransform();
	}

	/// <summary>Moves the focus point; offset.X is screen-right and offset.Y is screen-down, in metres.</summary>
	private void Pan(Vector2 offset)
	{
		var position = Position;
		position.X = Mathf.Clamp(position.X + offset.X, _bounds.Position.X, _bounds.End.X);
		position.Z = Mathf.Clamp(position.Z + offset.Y, _bounds.Position.Y, _bounds.End.Y);
		Position = position;
	}

	private static Vector2 KeyboardDirection()
	{
		var direction = Vector2.Zero;
		if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) direction.Y -= 1;
		if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) direction.Y += 1;
		if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) direction.X -= 1;
		if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) direction.X += 1;
		return direction;
	}

	private Vector2 EdgeScrollDirection()
	{
		if (EdgeScrollMargin <= 0 || _dragging || !GetWindow().HasFocus())
		{
			return Vector2.Zero;
		}

		var viewport = GetViewport();
		var size = viewport.GetVisibleRect().Size;
		var mouse = viewport.GetMousePosition();
		if (mouse.X < 0 || mouse.Y < 0 || mouse.X > size.X || mouse.Y > size.Y)
		{
			return Vector2.Zero;
		}

		var direction = Vector2.Zero;
		if (mouse.X < EdgeScrollMargin) direction.X -= 1;
		if (mouse.X > size.X - EdgeScrollMargin) direction.X += 1;
		if (mouse.Y < EdgeScrollMargin) direction.Y -= 1;
		if (mouse.Y > size.Y - EdgeScrollMargin) direction.Y += 1;
		return direction;
	}

	private void UpdateCameraTransform()
	{
		// Camera sits behind (+Z) and above the focus point, looking down at it, so screen-up is world -Z.
		float pitch = Mathf.DegToRad(PitchDegrees);
		_camera.Position = new Vector3(0, Mathf.Sin(pitch) * _distance, Mathf.Cos(pitch) * _distance);
		_camera.Rotation = new Vector3(-pitch, 0, 0);

		// The map spans from metres to 1000+ km, so the clip planes follow the zoom to keep depth precision.
		_camera.Near = Mathf.Max(0.5f, _distance * 0.002f);
		_camera.Far = _distance * 5f;
	}

	private Camera3D CreateCamera()
	{
		var camera = new Camera3D { Name = "Camera3D" };
		AddChild(camera);
		return camera;
	}
}
