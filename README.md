# Project Phields - Experiments

The Project Phields experiments repository contains the set of standardized protocols, operation instructions and metadata necessary for reproducible task control and acquisition of the foraging arena assay. The scripts contained in this repository should always represent as accurately as possible the automation routines and operational instructions used to log the experimental raw data for Project Phields. Each acquired dataset should have a reference to the specific hash or release from this repository which was used in the experiment.

## Deployment Instructions

Project Phields experiments run on the [Bonsai](https://bonsai-rx.org/) visual programming language. This repository includes installation scripts which will automatically download and configure a reproducible, self-contained, Bonsai environment to drive all acquisition and control systems in the environment. A few system dependencies and device drivers need to be installed separately, before running the environment configuration script.

### Prerequisites

These should only need to be installed once on a fresh new system, and are not required if simply refreshing the install or deploying to a new folder.

 * Windows 10
 * [Visual Studio Code](https://code.visualstudio.com/) (recommended for editing code scripts and git commits)
 * [.NET Framework 4.7.2 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/thank-you/net472-developer-pack-offline-installer) (required for intellisense when editing code scripts)
 * [Python 3.11](https://www.python.org/downloads/release/python-3119/) (required for bootstrapping Python environments)
 * [uv](https://docs.astral.sh/uv/) for python version, environment, and package dependency management.
 * [Git for Windows](https://gitforwindows.org/) (recommended for cloning and manipulating this repository)
 * [Visual C++ Redistributable for Visual Studio 2012](https://www.microsoft.com/en-us/download/details.aspx?id=30679) (native dependency for OpenCV)
 * [FTDI CDM Driver 2.12.28](https://www.ftdichip.com/Drivers/CDM/CDM21228_Setup.zip) (serial port drivers for HARP devices)
 * [Spinnaker SDK 1.29.0.5](https://www.flir.co.uk/support/products/spinnaker-sdk/#Downloads) (device drivers for FLIR cameras)
   * On FLIR website: `Download > archive > 1.29.0.5 > SpinnakerSDK_FULL_1.29.0.5_x64.exe`
 * [CUDA 11.3](https://developer.nvidia.com/cuda-11.3.0-download-archive) (for SLEAP multi-animal tracking)
   * Select Custom install and check `CUDA > Development` and `CUDA > Runtime` ONLY (uncheck everything else)
 


### Hardware Setup

The current workflows are designed to work with [FLIR Spinnaker Blackfly S cameras, BFS-U3-16S2M](https://www.flir.com/products/blackfly-s-usb3/?model=BFS-U3-16S2M-CS). Blackfly S cameras are configured by the experiment workflow itself.
Each camera should be connected to the main acquisition computer using USB 3.1, ideally making sure there are no more than 2 or 3 cameras per USB hub in the computer. Cameras are assigned using their unique serial numbers, so the order of connection is not important.

[Harp](https://harp-tech.org/) devices are synchronized in a hub and spoke topology. Cameras are triggered simultaneously using one of two independent PWM pulses generated by an [Camera controller](https://github.com/harp-tech/device.cameracontrollergen2) board (the `VideoController`). Hirose 6-pin GPIO cables can be used to connect the trigger line to the cameras.

#### PDU

The remote PDU can be configured using the CyberPower PDNU2 Network Utility. The default login for an unconfigured PDU are user: `cyber`, password: `cyber`. Configured PDUs use user: `phields`, password: `phields`.

### Environment Setup

The `bonsai` folder contains a snapshot of the runtime environment required to run experiments. The `setup.cmd` batch script is included in this repository to automate the download and configuration of this environment. Simply double-clicking on this script should launch the necessary [PowerShell](https://learn.microsoft.com/en-us/powershell/scripting/overview) commands as long as an active connection to the internet is available.

In case the configuration of the environment ever gets corrupted, you can revert the `bonsai` folder to its original state by deleting all the executable and package files and folders and re-running the `setup.cmd` script. This process may be automated in the future.

### Deploy

The `Deploy.ps1` contains scripts which can be installed in each local experiment repository to ensure that the environment is reset to the correct configuration whenever the repository switches to a different experimental branch. To install run the `Deploy.cmd` script from the `root` folder.

### Data Transfer

Data is continuously transferred to a CEPH partition by calling Robocopy from a scheduled task which runs periodically every hour. This task should be started as soon as the computer boots. For each experiment, a script to launch the periodic task is provided with the following naming convention `SystemStartup-<machine_name>.cmd`. This script should be installed in a new computer using the following steps:

  1. Press `Windows logo key + R` and type `shell:startup` in the run dialog box to open the system startup folder.
  2. Drag the script to the startup folder while holding the `Alt` key to create a new startup link.
  3. Press `Alt + Enter` in the new shortcut to access the link properties.
  4. Add `-p Synchronize=true` to the end of the `Target` string to ensure the Harp clock is reset to UTC time on system boot.
  5. Click the `OK` button to save the modified link properties.

The raw data folder name in CEPH should be the acquisition machine computer name.

The remote CEPH folder can be subsequently mounted on the local machine as a network drive and accessed as part of the file system.

### Experiment Workflows

All experiment-specific workflows required to run the experiment are stored in the `workflows` folder. We strongly recommend that each experiment-specific branch includes a `README.md` file describing what the protocol is about and how to configure or run it. The `.gitignore` file may be modified to exclude files according to the needs of each experiment.

It is recommended to keep every project within its own subfolder, and name the subfolder with the same name as the branch name. Multiple sub-folders are allowed if strict project separation is required, e.g. workflows running on different machines with different environments or incompatible extensions.
