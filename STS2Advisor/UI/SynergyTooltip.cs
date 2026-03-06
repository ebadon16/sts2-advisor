using System.Linq;
using System.Text;
using STS2Advisor.Core;

namespace STS2Advisor.UI
{
    public static class SynergyTooltip
    {
        public static string BuildCardTooltip(ScoredCard card)
        {
            var sb = new StringBuilder();

            foreach (string reason in card.SynergyReasons.Take(3))
                sb.AppendLine(reason);

            foreach (string reason in card.AntiSynergyReasons.Take(2))
                sb.AppendLine(reason);

            if (!string.IsNullOrEmpty(card.Notes))
                sb.AppendLine(card.Notes);

            return sb.ToString().TrimEnd();
        }

        public static string BuildRelicTooltip(ScoredRelic relic)
        {
            var sb = new StringBuilder();

            foreach (string reason in relic.SynergyReasons.Take(3))
                sb.AppendLine(reason);

            foreach (string reason in relic.AntiSynergyReasons.Take(2))
                sb.AppendLine(reason);

            if (!string.IsNullOrEmpty(relic.Notes))
                sb.AppendLine(relic.Notes);

            return sb.ToString().TrimEnd();
        }
    }
}
