using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using TMPro;
//using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;
//using static UnityEditor.Experimental.GraphView.GraphView;

public class Reaction_h2so4_cuo : MonoBehaviour
{
    [SerializeField] PourCuO salt;
    [SerializeField] PourH2so4 h2so4;
    public GameObject currentBerzelius;
    public TMP_Text canvasText;
    public Material material1;
    public Material material2;
    public Material material3;
    public GameObject popupWindow;
    public AudioSource audioSource_guidance;
    public AudioClip clip_guidance1;
    public AudioClip clip_guidance2;

    private DateTime timpInitial;
    private bool oneReaction = false;
    private bool showPopup = false;
    private bool audioSource1Started = false;
    private bool audioSource2Started = false;
    private Renderer currentSubstanceRenderer;
    private Material initialMaterial;
    void Start()
    {
        Transform substanceTransform = currentBerzelius != null ? currentBerzelius.transform.Find("Substance") : null;
        if (substanceTransform != null)
        {
            currentSubstanceRenderer = substanceTransform.GetComponent<Renderer>();
            if (currentSubstanceRenderer != null)
            {
                initialMaterial = currentSubstanceRenderer.material;
            }
        }
    }

    void Update()
    {
        if (h2so4.containsHCL == true && salt.containsCuO == false)
        {
            if (canvasText)
            {
                canvasText.text = "Now you can add Copper oxide. For this, grab the CuO beaker with the grip button or a hand pinch.";
            }
            if (!audioSource1Started)
            {
                audioSource_guidance.Stop();
                audioSource_guidance.PlayOneShot(clip_guidance1);
                audioSource1Started = true;
            }
        }
        if (oneReaction == false && salt.containsCuO == true && h2so4.containsHCL == true)
        {
            oneReaction = true;
            showPopup = true;
            canvasText.text = "Chemical reaction equation: H2SO4 + CuO = CuSO4 + H2O. Now you can learn another reaction.";
            if (!audioSource2Started)
            {
                audioSource_guidance.Stop();
                audioSource_guidance.PlayOneShot(clip_guidance2);
                audioSource2Started = true;
            }
            popupWindow.SetActive(true);
            timpInitial = DateTime.Now;
        }
        if (showPopup == true && (DateTime.Now - timpInitial).TotalSeconds >= 10)
        {
            showPopup = false;
            popupWindow.SetActive(false);
        }
        if (oneReaction && (DateTime.Now - timpInitial).TotalSeconds >= 2)
        {
            currentBerzelius.transform.Find("Substance").gameObject.GetComponent<Renderer>().material = material1;
        }
        if (oneReaction && (DateTime.Now - timpInitial).TotalSeconds >= 4)
        {
            currentBerzelius.transform.Find("Substance").gameObject.GetComponent<Renderer>().material = material2;
        }
        if (oneReaction && (DateTime.Now - timpInitial).TotalSeconds >= 6)
        {
            currentBerzelius.transform.Find("Substance").gameObject.GetComponent<Renderer>().material = material3;
        }
    }

    public void ResetReactionState()
    {
        oneReaction = false;
        showPopup = false;
        audioSource1Started = false;
        audioSource2Started = false;
        timpInitial = default;

        if (audioSource_guidance != null)
        {
            audioSource_guidance.Stop();
        }

        if (popupWindow != null)
        {
            popupWindow.SetActive(false);
        }

        if (currentSubstanceRenderer != null && initialMaterial != null)
        {
            currentSubstanceRenderer.material = initialMaterial;
        }
    }
}
