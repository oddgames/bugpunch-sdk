using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;

public class PopUpPrompt : MonoBehaviour
{
    public static PopUpPrompt instance;
    public static PopUpPrompt Instance { get { return instance; } }
    public bool isOn;
    public bool onPopupClose;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this.gameObject);
        }
        else
        {
            instance = this;
        }
    }

    public void OnPopUpConfirmation(string message)
    {
       
    }
   
    public void Enabled(bool active)
    {
        isOn = active;
        onPopupClose = true;
    }

}
