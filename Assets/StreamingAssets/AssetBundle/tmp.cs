using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class tmp : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        Debug.LogWarning("START!");
        var anim = GetComponent<Animator>();
        if (anim != null)
        {
            anim.Play("Attack");
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
