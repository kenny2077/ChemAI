# ChemAI

ChemAI is a Unity VR chemistry lab for Meta Quest 3. It combines interactive chemistry stations, grab-and-pour object handling, and a voice-first AI assistant that can listen to student questions, reason over the live simulation state, and answer with synthesized speech inside the headset.

This public repository contains the source-only release of the project: Unity scenes, runtime scripts, assets, package manifests, and project settings. Generated Unity cache folders, packaged APKs, archives, and live API credentials are intentionally excluded.

## Overview

The main experience in this repository is `LabScene 2 Free`, a free-order chemistry lab where four reaction stations are active at the same time. Students can move between stations, perform the intended experiment sequence at each one, and ask ChemAI for guidance about:

- what to do next
- which reagent is missing
- whether a station is complete, in progress, or failed
- what a visible change means
- whether an attempted action is unsafe or invalid

## Highlights

- **Voice-first ChemAI assistant**  
  Uses speech-to-text, state-aware response generation, and text-to-speech playback.

- **Free-order chemistry workflow**  
  The four supported stations can be completed in any order.

- **Quest-first interaction model**  
  Built for Meta Quest 3 with VR grab, movement, and pour-style manipulation.

- **Safety reminders and failure states**  
  The lab distinguishes between intended reactions, wrong mixtures, and explicitly dangerous actions.

- **Resettable live lab state**  
  The free-lab scene can be restored without reloading the entire scene.

## Experience Summary

The current free-lab flow is designed around:

- four intended chemistry stations placed around the table
- one blocked dangerous false experiment
- a ChemAI terminal that displays responses near the robot response area
- spoken AI feedback through Azure OpenAI speech synthesis
- independent station failure handling so one mistake does not end the whole lab

## Chemistry Stations

| Station | Intended sequence | Equation | Main observable behavior |
| --- | --- | --- | --- |
| Sulfuric acid + copper oxide | Pour sulfuric acid, then add copper oxide | `H2SO4 + CuO = CuSO4 + H2O` | Staged material transitions in the receiving beaker |
| Hydrochloric acid + sodium bicarbonate | Pour hydrochloric acid, then add sodium bicarbonate | `HCl + NaHCO3 = NaCl + H2O + CO2` | Bubbling / explosion-style effect with carbon dioxide release |
| Aluminum + iodine + water | Add aluminum, add iodine, then add water with the pipette | `2Al + 3I2 = 2AlI3` | Powder-state changes and dramatic particle effects |
| Calcium oxide + water | Pour water, add calcium oxide, then test with red litmus paper | `CaO + H2O = Ca(OH)2` | Calcium hydroxide formation and litmus-based basicity check |

## Safety Model

The free-lab implementation supports both hard failures and softer safety reminders.

### Station failures

Examples:

- contaminating the copper sulfate station with the wrong solid
- contaminating the sodium chloride station with the wrong solid
- adding pipette water too early in the aluminum iodide station
- testing litmus before the calcium hydroxide reaction is ready

These failures disable the affected station and require a reset.

### Dangerous false experiment

The current blocked false experiment is:

**Aluminum powder + sulfuric acid**

This is not part of the intended curriculum for `LabScene 2 Free`. If the sulfuric acid step has already started in the copper sulfate station and the aluminum powder bottle is brought into the sulfuric acid setup, the project is intended to trigger a warning rather than treat it as a valid experiment path.

**Safety reminder concept:**

> Do not add aluminum powder to sulfuric acid in this lab. That combination can produce hydrogen gas, which is highly flammable.

The warning is intended to:

- appear in the ChemAI response panel
- be spoken by ChemAI
- leave the station playable
- discourage the unsafe mix without turning it into a supported reaction

## ChemAI Voice Pipeline

ChemAI currently uses Azure OpenAI for three tasks:

1. **Speech to text** using a transcription deployment
2. **Reasoning / response generation** using a chat deployment
3. **Text to speech** using a speech deployment

At runtime, ChemAI can combine:

- the user’s spoken question
- live free-lab station state
- deterministic local fallback logic for common step-by-step questions
- proactive state-triggered hints and warnings

## Repository Layout

Main source directories:

- `Assets/Scenes/` - Unity scenes, including `LabScene 2 Free`
- `Assets/Scripts/` - runtime interaction, chemistry logic, ChemAI integration
- `Assets/Resources/` - publishable config template assets
- `Packages/` - Unity package manifest and lock data
- `ProjectSettings/` - Unity project configuration

Important runtime scripts for the free-lab experience:

- `Assets/Scripts/LabScene2FreeModeManager.cs`
- `Assets/Scripts/LabScene2FreeModeLayoutManager.cs`
- `Assets/Scripts/LabScene2FreeModeResetManager.cs`
- `Assets/Scripts/LabScene2FreeModeFailureManager.cs`
- `Assets/Scripts/LabScene2ExperimentStateHub.cs`
- `Assets/Scripts/LabScene2ChemAgentManager.cs`
- `Assets/Scripts/LabScene2AzureOpenAIClient.cs`

## Quick Start

### Unity version

This project is currently pinned to:

- `2022.3.62f3`

### Open the project

1. Open the repository root in Unity Hub using the pinned Unity version.
2. Let Unity regenerate local folders such as `Library/`, `Logs/`, and `UserSettings/`.
3. Open `Assets/Scenes/LabScene 2 Free.unity`.

For a clean clone or handoff, the required source folders are:

- `Assets/`
- `Packages/`
- `ProjectSettings/`

## Local ChemAI Configuration

This repository is intentionally published without live OpenAI credentials.

The committed template file is:

- `Assets/Resources/LabScene2OpenAIConfigTemplate.json`

To enable ChemAI locally:

1. Copy the template into a new file named `Assets/Resources/LabScene2OpenAIConfigLocal.json`
2. Fill in your own Azure OpenAI endpoint and key
3. Keep that local file out of version control

Example structure:

```json
{
  "endpoint": "https://your-resource-name.openai.azure.com/openai/v1",
  "apiKey": "replace-with-your-azure-openai-key",
  "chatModel": "gpt-5",
  "ttsModel": "gpt-4o-mini-tts",
  "transcriptionModel": "gpt-4o-mini-transcribe",
  "voice": "alloy"
}
```

`LabScene2ChemAgentManager` will load this local resource at runtime if it exists. The local config file is ignored by git and should never be committed.

## Repo Hygiene

The repository is configured to exclude:

- Unity-generated folders such as `Library/`, `Logs/`, and `UserSettings/`
- packaged builds such as `.apk`
- local archives such as `.zip`
- local recovery artifacts
- the local ChemAI credential file

## Build Target

The active target experience for this repository is a Meta Quest 3 VR chemistry lab, with `LabScene 2 Free` as the main free-play chemistry scene.
