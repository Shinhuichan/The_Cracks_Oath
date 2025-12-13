using UnityEngine;

public abstract class SingletonBehaviour<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
    // 🚫 종료 중인지 체크하는 플래그
    private static bool _isQuitting = false;
    private static object _lock = new object();

    public static T I
    {
        get
        {
            if (_isQuitting)
            {
                // 종료 중이면 null 반환하여 재생성 방지
                return null;
            }

            lock (_lock) // 멀티스레드 안전성 확보 (선택사항)
            {
                if (_instance != null)
                    return _instance;

                _instance = FindFirstObjectByType<T>();

                if (_instance != null)
                {
                    return _instance;
                }

                // 인스턴스가 없을 경우 생성
                GameObject singletonObject = new GameObject(typeof(T).Name);
                _instance = singletonObject.AddComponent<T>();
                return _instance;
            }
        }
    }

    protected abstract bool IsDontDestroy();

    protected virtual void Awake()
    {
        if (_instance == null)
        {
            _instance = this as T;
            if (IsDontDestroy())
            {
                DontDestroyOnLoad(this.gameObject);
            }
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    protected virtual void OnApplicationQuit()
    {
        _isQuitting = true;
    }
}