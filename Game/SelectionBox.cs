using Godot;

namespace WarOfResilience.Game;

/// <summary>The rectangle drawn while dragging to select units.</summary>
public partial class SelectionBox : Control
{
	private Rect2? _rect;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsPreset(LayoutPreset.FullRect);
	}

	public void Show(Vector2 from, Vector2 to)
	{
		_rect = new Rect2(from, to - from).Abs();
		QueueRedraw();
	}

	public void Clear()
	{
		_rect = null;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_rect is { } rect)
		{
			DrawRect(rect, new Color(0.3f, 1f, 0.4f, 0.12f));
			DrawRect(rect, new Color(0.3f, 1f, 0.4f, 0.9f), filled: false, width: 1.5f);
		}
	}
}
