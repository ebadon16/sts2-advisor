using Godot;

namespace QuestceSpire.UI;

internal class OverlayInputHandler : Node
{
	private OverlayManager _owner;
	private double _checkTimer;

	public OverlayInputHandler(OverlayManager owner)
	{
		_owner = owner;
	}

	public override void _Input(InputEvent ev)
	{
		_owner.HandleInput(ev);
	}

	public override void _Process(double delta)
	{
		_checkTimer += delta;
		if (_checkTimer >= 1.0)
		{
			_checkTimer = 0;
			_owner.CheckForStaleScreen();
		}
	}
}
