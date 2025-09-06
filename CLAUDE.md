# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Unity 2022.x project demonstrating Magic Leap 2 colocation using Photon Fusion networking. The project showcases two main colocation methods:

1. **Marker-based Tracking**: Uses AprilTag markers (tag36_11 family) for shared spatial reference
2. **Magic Leap Anchors**: Uses Magic Leap's spatial anchors and AR Cloud Spaces for colocation

## Key Dependencies

- **Magic Leap SDK 1.8.0** (`com.magicleap.unitysdk`)
- **Unity XR AR Foundation 5.0.5** for XR functionality  
- **Universal Render Pipeline (URP) 14.0.6** for rendering
- **AprilTag Unity package** (`jp.keijiro.apriltag`) for marker detection
- **Unity MCP Bridge** (`com.coplaydev.unity-mcp`) for debugging/tooling
- **Photon Fusion SDK** (imported as Unity package, not via Package Manager)

## Architecture

### Core Structure
```
Assets/MagicLeap/PhotonFusionExample/
├── Scripts/
│   ├── MagicLeapSpaces/     # Magic Leap spatial anchor management
│   ├── MarkerTracking/      # AprilTag marker detection and tracking
│   ├── PhotonFusion/        # Photon Fusion networking scripts
│   └── Utilities/           # Helper scripts (e.g., ThreadDispatcher)
├── Scenes/                  # Example scenes for different colocation methods
├── Prefabs/                 # Reusable GameObjects for networked elements
└── Models/                  # Avatar models for multiplayer visualization
```

### Key Systems

**Colocation Pipeline**: 
1. Marker detection OR Magic Leap anchor localization establishes shared world origin
2. Photon Fusion handles networked object synchronization
3. Multiple users see consistent virtual content placement

**Networking**: Built on Photon Fusion with custom scripts in `Scripts/PhotonFusion/` handling player spawning, object synchronization, and session management.

**Platform Targets**: 
- Primary: Magic Leap 2 (Android with ML2 SDK)
- Secondary: Desktop for development/testing (with webcam for marker tracking)

## Development Workflow

### Building for Magic Leap 2
1. Set build target to Android
2. Ensure Magic Leap XR settings are configured in Project Settings > XR Plug-in Management
3. Configure Photon Fusion App ID in Photon settings
4. For marker colocation: Print AprilTag marker from `Marker/tag36_11_00000_size-170-millimeters.png`
5. For anchor colocation: Ensure devices are localized to same AR Cloud Space or shared Space

### Scripting Defines
The project uses these key scripting defines:
- `APRIL_TAGS`: Enables AprilTag marker detection
- `MAGICLEAP`: Magic Leap specific functionality
- `FUSION_WEAVER`: Photon Fusion code generation (Android/Standalone)
- `ANDROID_X86_64`: Android platform specific (for ML2)

### Documentation
- Main README: `/README.md` - Setup and usage instructions
- Project-specific README: `/Assets/MagicLeap/PhotonFusionExample/README.md` - Detailed project structure
- Integration docs: `/docs/integration/` - Analysis and implementation guides

## Important Notes

- **Archived Repository**: This is an archived Magic Leap example project, no longer actively maintained
- **AprilTag License**: Uses modified AprilTag Unity package under BSD 2-Clause license
- **Thread Safety**: Uses ThreadDispatcher utility for Unity main thread operations from background threads
- **Mixed Reality**: Designed for shared AR experiences where multiple users see synchronized virtual content in physical space