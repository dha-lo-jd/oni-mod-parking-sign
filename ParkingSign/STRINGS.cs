using TUNING;

namespace ParkingSign;

public static class STRINGS
{
    public static class BUILDING
    {
        public static class STATUSITEMS
        {
            public static class PARKINGSIGNACTIVE
            {
                public static LocString NAME = "Active";
                public static LocString TOOLTIP = "The Parking Sign is receiving a Green signal.";
            }

            public static class PARKINGSIGNINACTIVE
            {
                public static LocString NAME = "Inactive";
                public static LocString TOOLTIP = "The Parking Sign is not receiving a Green signal.";
            }
        }
    }

    public static class UI
    {
        public static class PARKINGSIGN
        {
            public static LocString SIDESCREEN_TITLE = "Parking Sign";
        }
    }

    public static class BUILDINGS
    {
        public static class PREFABS
        {
            public static class PARKINGSIGN
            {
                public static LocString NAME = "Parking Sign";
                public static LocString DESC = "A sign for marking a parking space.";
                public static LocString EFFECT = "Becomes Active when receiving a <color=#00FF00>Green</color> signal.";
                public static LocString LOGIC_PORT = "Parking Sign";
                public static LocString LOGIC_PORT_ACTIVE = "Active";
                public static LocString LOGIC_PORT_INACTIVE = "Inactive";
            }
        }
    }
}