import aeon.io.reader as _reader
from aeon.schema import core
from aeon.schema.streams import Stream, StreamGroup

class Motor(Stream):
    def __init__(self, path):
        super().__init__(_reader.Csv(f"{path}*", columns=["event.type", "event.message"]))

class BinaryTrackingPosition(Stream):
    """Position tracking data for the specified camera."""
    def __init__(self, pattern):
        """Initializes the Position stream."""
        super().__init__(_reader.Position(f"{pattern}_210_*"))

class BinaryTrackingState(Stream):
    """Position tracking data for the specified camera."""
    def __init__(self, pattern):
        """Initializes the Position stream."""
        super().__init__(_reader.Harp(f"{pattern}_211_*",columns=["minimizedDistance","crossings"]))

class HeadTail(Stream):
    def __init__(self, pattern):
        super().__init__(_reader.Harp(f"{pattern}_220_*",columns=["centroid.x","centroid.y","head.x","head.y","tail.x","tail.y","heading","velocity.x","velocity.y","id"]))

class LoomRegion(Stream):
    def __init__(self, pattern):
        super().__init__(_reader.Harp(f"{pattern}_240_*",columns=["timeInRegion","id"]))

class LoomAngle(Stream):
    def __init__(self, pattern):
            super().__init__(_reader.Harp(f"{pattern}_230_*", columns=["loomAngle","id"]))

class Environment(StreamGroup):
    def __init__(self, path):
        super().__init__(path)

    EnvironmentState = core.EnvironmentState
  
    MessageLog = core.MessageLog


class SubjectData(StreamGroup):
    def __init__(self, path):
        super().__init__(path)

    class SubjectState(Stream):
        def __init__(self, path):
            super().__init__(_reader.Csv(f"{path}_SubjectState_*", columns=["id", "weight", "type"]))