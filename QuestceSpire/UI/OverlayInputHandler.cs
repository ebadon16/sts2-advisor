using Godot;

namespace QuestceSpire.UI;

internal class OverlayInputHandler : Node
{
	private OverlayManager _owner;
	private double _checkTimer;
	private int _stabilizeTicks;

	public OverlayInputHandler(OverlayManager owner)
	{
		_owner = owner;
		ProcessMode = ProcessModeEnum.Always;
		_stabilizeTicks = 0;
	}

	public override void _UnhandledKeyInput(InputEvent ev)
	{
		if (ev is InputEventKey { Pressed: not false, Echo: false } key)
		{
			if (key.Keycode == Key.F10 || key.PhysicalKeycode == Key.F10)
			{
				_owner.ToggleDebugOverlay();
				return;
			}
		}
		_owner.HandleInput(ev);
	}

	public override void _Input(InputEvent ev)
	{
		// Settings menu close (click-outside / Escape) still needs _Input
		// since it should intercept before game handles it
		if (ev is InputEventMouseButton || (ev is InputEventKey key && key.Keycode == Key.Escape))
			_owner.HandleSettingsClose(ev);
	}

	public override void _Process(double delta)
	{
		// Layout stabilization for the first ~1 second after build
		if (_stabilizeTicks < 5)
		{
			_stabilizeTicks++;
			_owner.StabilizeLayout();
		}

		_checkTimer += delta;
		if (_checkTimer >= 1.0)
		{
			_checkTimer = 0;
			_owner.CheckForStaleScreen();
		}
	}
}
