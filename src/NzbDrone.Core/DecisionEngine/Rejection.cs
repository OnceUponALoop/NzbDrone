using System;

namespace NzbDrone.Core.DecisionEngine
{
    public class Rejection
    {
        public String Reason { get; set; }
        public RejectionType Type { get; set; }

        public Rejection(string reason)
        {
            Reason = reason;
            Type = RejectionType.Permanent;
        }

        public Rejection(string reason, RejectionType type)
        {
            Reason = reason;
            Type = type;
        }

        public override string ToString()
        {
            return String.Format("[{0}] {1}", Type, Reason);
        }
    }
}
