import aeon.io.reader as _reader
from aeon.schema import core
from aeon.schema.streams import Stream, StreamGroup

class Motor(Stream):
    def __init__(self, path):
        super().__init__(_reader.Csv(f"{path}*", columns=["event.type", "event.message"]))

class HeadTail(Stream):
    def __init__(self, path):
        super().__init__(_reader.Csv(f"{path}_HeadTail_*", columns=["id", "centroid.x", "centroid.y", "head.x", "head.y", "tail.x", "tail.y", "heading", "velocity.x", "velocity.y"]))

class BinaryRegion(Stream):
    def __init__(self, path):
        super().__init__(_reader.Csv(f"{path}_BinaryRegions_*", columns=["id", "crossings", "centroid.x", "centroid.y"]))

class LoomRegion(Stream):
    def __init__(self, path):
        super().__init__(_reader.Csv(f"{path}_LoomRegion_*", columns=["id", "inLoomDuration"]))

class LoomAngle(Stream):
    def __init__(self, path):
            super().__init__(_reader.Csv(f"{path}_LoomAngle_*", columns=["id", "loomAngle"]))

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