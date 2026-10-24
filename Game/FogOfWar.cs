using System.Linq;
using Godot;
using WarOfResilience.Sim.Core;
using WarOfResilience.Sim.Units;
using WarOfResilience.Sim.World;

namespace WarOfResilience.Game;

/// <summary>
/// Fog of war for the local player: a low-resolution texture over the map, bright over their own
/// country and wherever they see now, dimmer where they have looked before and dark where they
/// never have. The terrain and
/// water shaders darken the ground with it. Only reads from Sim.
/// </summary>
public partial class FogOfWar : Node
{
	/// <summary>Metres per fog texel.</summary>
	private const float TexelSize = 2_000f;

	private const double RefreshSeconds = 0.25;
	private const byte Unexplored = 0;
	private const byte Explored = 120;
	private const byte Visible = 255;

	private Vision _vision = null!;
	private int _playerId;
	private int _width;
	private int _depth;
	private byte[] _explored = null!;
	private bool[] _ownLand = null!;
	private byte[] _pixels = null!;
	private Image _image = null!;
	private ImageTexture _texture = null!;
	private double _sinceRefresh = RefreshSeconds;

	public Texture2D Texture => _texture;

	public void Setup(Vision vision, int playerId, Terrain terrain, Territory territory)
	{
		float sizeX = (float)terrain.SizeX.ToDouble();
		float sizeZ = (float)terrain.SizeZ.ToDouble();
		_vision = vision;
		_playerId = playerId;
		_width = Mathf.CeilToInt(sizeX / TexelSize) + 1;
		_depth = Mathf.CeilToInt(sizeZ / TexelSize) + 1;
		_explored = new byte[_width * _depth];
		_pixels = new byte[_width * _depth];
		_ownLand = new bool[_width * _depth];
		for (int z = 0; z < _depth; z++)
		{
			for (int x = 0; x < _width; x++)
			{
				var wx = Fixed.FromInt((int)(x * TexelSize));
				var wz = Fixed.FromInt((int)(z * TexelSize));
				_ownLand[z * _width + x] = territory.OwnerAt(terrain, wx, wz) == playerId;
			}
		}

		_image = Image.CreateFromData(_width, _depth, false, Image.Format.L8, _pixels);
		_texture = ImageTexture.CreateFromImage(_image);
		Refresh();
	}

	public override void _Process(double delta)
	{
		_sinceRefresh += delta;
		if (_vision != null && _sinceRefresh >= RefreshSeconds)
		{
			_sinceRefresh = 0;
			Refresh();
		}
	}

	private void Refresh()
	{
		for (int i = 0; i < _pixels.Length; i++)
		{
			_pixels[i] = _ownLand[i] ? Visible : _explored[i] != 0 ? Explored : Unexplored;
		}

		foreach (var (sx, sz, radius) in _vision.SourcesOf(_playerId).ToList())
		{
			// Fill each source's circle; a texel counts if its centre is in sight.
			float cx = (float)sx.ToDouble() / TexelSize;
			float cz = (float)sz.ToDouble() / TexelSize;
			float r = radius / TexelSize + 0.5f;
			for (int z = Mathf.Max(0, (int)(cz - r)); z <= Mathf.Min(_depth - 1, (int)(cz + r) + 1); z++)
			{
				for (int x = Mathf.Max(0, (int)(cx - r)); x <= Mathf.Min(_width - 1, (int)(cx + r) + 1); x++)
				{
					float dx = x - cx;
					float dz = z - cz;
					if (dx * dx + dz * dz <= r * r)
					{
						_pixels[z * _width + x] = Visible;
						_explored[z * _width + x] = 1;
					}
				}
			}
		}

		_image.SetData(_width, _depth, false, Image.Format.L8, _pixels);
		_texture.Update(_image);
	}
}
