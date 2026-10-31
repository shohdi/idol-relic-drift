# Validation record

Recorded on 2026-10-31 using Unity **6000.2.6f1**, Windows x64, Direct3D 11, and an NVIDIA GeForce RTX 4060 Laptop GPU.

## Completed checks

- All three source images were generated specifically for this game using the built-in OpenAI image-generation tool. They are included under `Assets/Game/Art/Sources` with the prompts recorded in `IMAGE_PROMPTS.md`.
- Idol Studio generated the complete original splat outputs: **65,536 courier**, **32,768 sun-crystal**, and **32,768 void-rock** Gaussians. All used synthetic mode, 50 steps, guidance 3 and time shift 3 with the recorded seeds.
- `Tools/Verify-Provenance.ps1` passed after generation and after the Unity build. Every source PNG and output PLY hash matches its generation record; no Gaussian data was hand-edited or decimated for the game.
- Unity project audit passed: exactly three `.idolply` models and **zero imported mesh assets**. The bootstrap scene has no modeled geometry. Runtime actors use the original Idol assets and contain no `MeshRenderer`, `MeshFilter`, or `SkinnedMeshRenderer` substitutes.
- Actual Unity C# import/compile, D3D11 shader compilation, development-player build, and **release Windows player build succeeded**.
- The final development player passed **28 checks**, including actual spawned pickups and rock collisions through the normal movement loop, score/multiplier changes, shield loss, pause/resume, frozen paused time, completion, game over, restart, saved best score and original Gaussian submission to the GPU.
- Runtime error/exception list was empty, and any pre-existing user highscore/mute preferences were restored after validation.
- Title, gameplay, pause, delivery-complete and game-over screens were captured and visually inspected at **1280×720**. Courier orientation and the title composition were tuned using only rigid transforms. Final dialogs have no clipped controls or overlapping text.
- The original Idol Studio repository remained at commit `1ff3ee61a2f719637cfb6ac8113b2ec9494fa082` with a clean working tree. This game was made in a separate folder and repository.

## Capture method

The development player ran real gameplay and rendered its actual camera into a RenderTexture. A second UI-only camera composed its existing HUD after the world pass, preserving overlay ordering while allowing unattended capture. These screenshots are GPU-rendered game frames, not mockups. The validation-only capture path is inactive during ordinary play.

Images: [title](Images/title.png), [gameplay](Images/playing.png), [pause](Images/paused.png), [complete](Images/completed.png), [game over](Images/game-over.png).

## Reproduce

```powershell
./Tools/Verify-Provenance.ps1
./Tools/Run-Checks.ps1
./Tools/Build.ps1
```

`Run-Checks.ps1` writes development-player logs, screenshots and its JSON report under `Builds/Checks`. `Build.ps1` writes the release player under `Builds/Windows`. These generated directories are excluded from source control; the public release ZIP contains the playable Windows build.

GitHub Actions verifies the source/output asset hashes and the absence of conventional 3D assets or credential files. Unity player builds and gameplay checks were run locally using the installed licensed Editor; the CI workflow does not claim to rebuild Unity without an Editor license.

## Scope and limitations

This is a small single-player arcade game, not a general animation or mesh-generation system. Splats do not provide a watertight mesh. The renderer preserves all points and sorts separated objects approximately by center; interpenetrating transparent clouds are outside the intended scene design. Direct3D 11 Windows x64 was tested; other platforms and GPUs were not validated. The game has no online service and requires no network access to play.

The exact 2D source PNGs and generated 3D files are supplied. Their provenance is recorded, but future image/model generation on different tools/backends need not be bit-identical. No Python, Blender, downloaded 3D models or hand-modeled world assets were used.
