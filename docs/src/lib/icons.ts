import {
  BookOpen,
  Construction,
  Fingerprint,
  Languages,
  Library,
  Map,
  Rocket,
  Sparkles,
  View,
  Wrench,
  type LucideIcon,
} from "lucide-react";

const iconMap: Record<string, LucideIcon> = {
  BookOpen,
  Construction,
  Fingerprint,
  Languages,
  Library,
  Map,
  Rocket,
  Sparkles,
  View,
  Wrench,
};

export function sectionIcon(name: string): LucideIcon {
  return iconMap[name] ?? Rocket;
}
