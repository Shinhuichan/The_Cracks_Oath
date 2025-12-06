using UnityEngine;
using System.Collections.Generic;

public class ChangingUI : MonoBehaviour
{
    [SerializeField] private List<GameObject> uiPanels;
    private int currentIndex = 0;
    [SerializeField] private float changeInterval = 5f;
    private float timer = 0f;
    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= changeInterval)
        {
            timer = 0f;
            uiPanels[currentIndex].SetActive(false);
            currentIndex = (currentIndex + 1) % uiPanels.Count;
            uiPanels[currentIndex].SetActive(true);
        }
    }
}
