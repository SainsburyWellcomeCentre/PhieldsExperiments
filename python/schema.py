from dotmap import DotMap

import aeon.schema.core as stream
import phields_grant_jan_2025 as phields
from aeon.schema.streams import Device

phields01 = DotMap(
    [   
        Device("Metadata", stream.Metadata),
        Device("CameraTop", stream.Video, stream.Position, phields.HeadTail, phields.BinaryRegion, phields.LoomAngle, phields.LoomRegion),
        Device("CameraSide", stream.Video),
        Device("Motor", phields.Motor)
    ]
)