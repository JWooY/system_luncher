using UnityEngine;

// 제네릭 싱글톤 패턴을 구현한 베이스 클래스
public class SingletonBehaviour<T> : MonoBehaviour where T : SingletonBehaviour<T>
{
    // 씬 전환 시 삭제할지 여부
    protected bool m_IsDestroyOnLoad = false;

    // 이 클래스의 스태틱 인스턴스 변수
    protected static T m_Instance;

    // 인스턴스 접근용 프로퍼티
    public static T Instance
    {
        get { return m_Instance; }
    }

    // 초기화를 위해 Awake에서 Init 호출
    private void Awake()
    {
        Init();
    }

    // 싱글톤 초기화 로직
    protected virtual void Init()
    {
        if(m_Instance == null)
        {
            m_Instance = (T)this;

            // 씬 전환 시 유지할지 결정
            if(!m_IsDestroyOnLoad)
            {
                DontDestroyOnLoad(this);
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // 오브젝트 파괴 시 호출
    protected virtual void OnDestroy()
    {
        Dispose();
    }

    // 정리 작업 수행
    protected virtual void Dispose()
    {
        m_Instance = null;
    }
}
