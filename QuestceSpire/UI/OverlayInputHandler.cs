using Godot;

namespace QuestceSpire.UI;

internal class OverlayInputHandler : Node
{
	private OverlayManager _owner;

	public OverlayInputHandler(OverlayManager owner)
	{
		_owner = owner;
	}

	public override void _Input(InputEvent ev)
	{
		_owner.HandleInput(ev);
	}
}
