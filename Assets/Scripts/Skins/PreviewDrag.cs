using UnityEngine;
using UnityEngine.EventSystems;

/// Dragging across the preview turns the weapon.
public class PreviewDrag : MonoBehaviour, IDragHandler
{
    public PreviewStage stage;

    public void OnDrag(PointerEventData e)
    {
        if (stage != null)
            stage.TurnBy(-e.delta.x * 0.4f);
    }
}
