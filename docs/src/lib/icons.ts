import {
  BookOpen,
  Fingerprint,
  Languages,
  Library,
  Map,
  Rocket,
  ScrollText,
  Sparkles,
  View,
  Wrench,
  type LucideIcon,
} from "lucide-react";

const iconMap: Record<string, LucideIcon> = {
  BookOpen,
  Fingerprint,
  Languages,
  Library,
  Map,
  Rocket,
  ScrollText,
  Sparkles,
  View,
  Wrench,
};

export function sectionIcon(name: string): LucideIcon {
  return iconMap[name] ?? Rocket;
}
