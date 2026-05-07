using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class reactionCaOH : MonoBehaviour
{
    [SerializeField] PourCuO salt;
    [SerializeField] PourSubstance h2o;
    public GameObject CaOHPivot;
    public GameObject TurnesolPivot;
    public GameObject wetLitmusPaper;
    public GameObject wetLitmusPaper1;
    public Material blueLitmusMaterial;

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
    public AudioClip clip_guidance3;

    private DateTime timpInitial;
    private bool isPlaying = false;
    private bool explosionActive = false;
    private bool oneExplosion = false;
    private bool showPopup = false;
    private bool done = false;
    private bool audioSource1Started = false;
    private bool audioSource2Started = false;
    private bool audioSource3Started = false;
    private Renderer currentSubstanceRenderer;
    private Material initialMaterial;
    private Material initialWetLitmusMaterial;
    private Material initialWetLitmusMaterial1;
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

        if (wetLitmusPaper != null)
        {
            Renderer renderer = wetLitmusPaper.GetComponent<Renderer>();
            if (renderer != null)
            {
                initialWetLitmusMaterial = renderer.material;
            }
        }

        if (wetLitmusPaper1 != null)
        {
            Renderer renderer = wetLitmusPaper1.GetComponent<Renderer>();
            if (renderer != null)
            {
                initialWetLitmusMaterial1 = renderer.material;
            }
        }
    }

    void Update()
    {
        if (h2o.containsWater == true && salt.containsCuO == false)
        {
            if (canvasText)
            {
                canvasText.text = "Now you can add Calcium oxide. For this, grab the CaO beaker with the grip button or a hand pinch.";
            }
            if (!audioSource1Started)
            {
                audioSource_guidance.Stop();
                audioSource_guidance.PlayOneShot(clip_guidance1);
                audioSource1Started = true;
            }
        }
        if (oneExplosion == false && salt.containsCuO == true && h2o.containsWater == true)
        {
            done = true;
            explosionActive = true;
            explosionGameObject.SetActive(true);
            showPopup = true;
            canvasText.text = "Chemical reaction equation: CaO + H2O = Ca(OH)2. Now you can check the basicity of the substance with red litmus paper.";
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
        if (Math.Abs(TurnesolPivot.transform.position.z - CaOHPivot.transform.position.z) < 0.05 &&
            Math.Abs(TurnesolPivot.transform.position.x - CaOHPivot.transform.position.x) < 0.05 &&
            Math.Abs(TurnesolPivot.transform.position.y - CaOHPivot.transform.position.y) < 0.01 &&
            done == true)
        {
            wetLitmusPaper.gameObject.GetComponent<Renderer>().material = blueLitmusMaterial;
            wetLitmusPaper1.gameObject.GetComponent<Renderer>().material = blueLitmusMaterial;
            popupWindow.SetActive(true);
            canvasText.text = "You have successfully checked the basicity of the substance. Now you can learn another reaction.";
            if (!audioSource3Started)
            {
                audioSource_guidance.Stop();
                audioSource_guidance.PlayOneShot(clip_guidance3);
                audioSource3Started = true;
            }
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
        done = false;
        audioSource1Started = false;
        audioSource2Started = false;
        audioSource3Started = false;
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

        if (wetLitmusPaper != null && initialWetLitmusMaterial != null)
        {
            Renderer renderer = wetLitmusPaper.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = initialWetLitmusMaterial;
            }
        }

        if (wetLitmusPaper1 != null && initialWetLitmusMaterial1 != null)
        {
            Renderer renderer = wetLitmusPaper1.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = initialWetLitmusMaterial1;
            }
        }
    }
}
