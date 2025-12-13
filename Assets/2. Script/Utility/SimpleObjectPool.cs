using System.Collections.Generic;
using UnityEngine;

public class SimpleObjectPool : MonoBehaviour
{
    // Inspector에서 프리팹 할당
    public GameObject prefab;
    public Transform parentTransform;
    public int initialSize = 20;

    private Queue<GameObject> pool = new Queue<GameObject>();
    private List<GameObject> activeObjects = new List<GameObject>();

    void Awake()
    {
        InitializePool();
    }

    void InitializePool()
    {
        for (int i = 0; i < initialSize; i++)
        {
            CreateNewObject();
        }
    }

    private GameObject CreateNewObject()
    {
        GameObject obj = Instantiate(prefab, parentTransform);
        obj.SetActive(false);
        pool.Enqueue(obj);
        return obj;
    }

    public GameObject Get()
    {
        if (pool.Count == 0) CreateNewObject();

        GameObject obj = pool.Dequeue();
        obj.SetActive(true);
        activeObjects.Add(obj);
        return obj;
    }

    // 사용 중인 모든 오브젝트를 풀로 반환 (RankingManager에서 Refresh 할 때 유용)
    public void ReturnAll()
    {
        foreach (var obj in activeObjects)
        {
            obj.SetActive(false);
            pool.Enqueue(obj);
        }
        activeObjects.Clear();
    }
}