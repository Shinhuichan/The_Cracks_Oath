using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameObjectFinder
{
    // 현재 Scene에 있는 모든 Object(비활성화 포함) 중에서 검색
    public static T FindObjectOfTypeIncludingInactive<T>() where T : Component
    {

        foreach (GameObject rootObject in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            T foundComponent = rootObject.GetComponentInChildren<T>(true);
            if (foundComponent != null)
            {
                return foundComponent; // 찾았으면 바로 반환
            }
        }
        return null; // 못 찾으면 null 반환
    }
}