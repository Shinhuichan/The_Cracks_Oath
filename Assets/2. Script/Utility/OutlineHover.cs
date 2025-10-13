using UnityEngine;
using UnityEngine.EventSystems;

public class OutlineHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Material originalMaterial;
    [SerializeField] private Material outlineMaterial;
    private Renderer rend;

    void Start()
    {
        rend = GetComponent<Renderer>();
        originalMaterial = rend.material;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        rend.material = outlineMaterial;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        rend.material = originalMaterial;
    }
}