using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using TMPro;
//using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;
//using static UnityEditor.Experimental.GraphView.GraphView;

public class Reaction_hcl_nahco3 : MonoBehaviour
{
    [SerializeField] PourNahco3 salt;
    [SerializeField] PourHCL hcl;
    public GameObject currentBerzelius;
    public Material material;
    public ParticleSystem explosion;
    public GameObject explosionGameObject;
    public TMP_Text canvasText;
    public GameObject popupWindow;
    public AudioSource audioSource;
    public AudioClip clip;
    public AudioSource audioSource_guidance;
    public AudioClip clip_guidance1;
    public AudioClip clip_guidance2;

    private DateTime timpInitial;
    private bool isPlaying = false;
    private bool explosionActive = false;
    private bool oneExplosion = false;
    private bool showPopup = false;
    private bool audioSource1Started = false;
    private bool audioSource2Started = false;
    private Renderer currentSubstanceRenderer;
    private Material initialMaterial;
    private bool initialExplosionObjectActive;

    void Start()
    {
        explosionGameObject.SetActive(false);
        explosion.Stop();
        explosion.Clear();

        initialExplosionObjectActive = explosionGameObject != null && explosionGameObject.activeSelf;
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
        if (hcl.containsHCL == true && salt.containsNahco3 == false)
        {
            if (canvasText)
            {
                canvasText.text = "Now you can add Sodium bicarbonate. For this, grab the NaHCO3 beaker with the grip button or a hand pinch.";
            }
            if (!audioSource1Started)
            {
                audioSource_guidance.Stop();
                audioSource_guidance.PlayOneShot(clip_guidance1);
                audioSource1Started = true;
            }
        }
        if (oneExplosion == false && salt.containsNahco3 == true && hcl.containsHCL == true)
        {
            explosionActive = true;
            explosionGameObject.SetActive(true);
            showPopup = true;
            canvasText.text = "Chemical reaction equation: HCl + NaHCO3 = NaCl + H2O + CO2. Now you can learn another reaction.";
            if (!audioSource2Started)
            {
                audioSource_guidance.Stop();
                audioSource_guidance.PlayOneShot(clip_guidance2);
                audioSource2Started = true;
            }
            popupWindow.SetActive(true);
            oneExplosion = true;
            timpInitial = DateTime.Now;
            explosion.Clear();
            explosion.Play();
            if (!isPlaying)
            {
                StartCoroutine(PlaySoundRepeatedly());
            }
        }
        if (explosionActive && (DateTime.Now - timpInitial).TotalSeconds >= 6)
        {
            explosionActive = false;
            explosionGameObject.SetActive(false);
            currentBerzelius.transform.Find("Substance").gameObject.GetComponent<Renderer>().material = material;
            explosion.Stop();
            explosion.Clear();
            isPlaying = false;
            audioSource.Stop();
        }
        if (showPopup == true && (DateTime.Now - timpInitial).TotalSeconds >= 10)
        {
            showPopup = false;
            popupWindow.SetActive(false);
        }
    }

    IEnumerator PlaySoundRepeatedly()
    {
        isPlaying = true;
        while (isPlaying)
        {
            audioSource.PlayOneShot(clip);
            yield return new WaitForSeconds(clip.length);
        }
    }

    public void ResetReactionState()
    {
        isPlaying = false;
        explosionActive = false;
        oneExplosion = false;
        showPopup = false;
        audioSource1Started = false;
        audioSource2Started = false;
        timpInitial = default;

        if (explosion != null)
        {
            explosion.Stop();
            explosion.Clear();
        }

        if (explosionGameObject != null)
        {
            explosionGameObject.SetActive(initialExplosionObjectActive);
        }

        if (audioSource != null)
        {
            audioSource.Stop();
        }

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
