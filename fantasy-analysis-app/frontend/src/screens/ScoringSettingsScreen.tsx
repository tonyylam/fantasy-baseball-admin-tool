import { useEffect, useState } from "react";
import { getAvailableScoringCategories, getScoringSettings, saveScoringSettings } from "../api/client";
import type { ScoringCategoryOption, ScoringSettings } from "../types";

interface ScoringSettingsScreenProps {
  onSaved: (settings: ScoringSettings) => void;
}

export function ScoringSettingsScreen({ onSaved }: ScoringSettingsScreenProps) {
  const [availableCategories, setAvailableCategories] = useState<ScoringCategoryOption[]>([]);
  const [hittingKeys, setHittingKeys] = useState<string[]>([]);
  const [pitchingKeys, setPitchingKeys] = useState<string[]>([]);
  const [rosterSlots, setRosterSlots] = useState<[string, number][]>([]);
  const [saving, setSaving] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([getAvailableScoringCategories(), getScoringSettings()])
      .then(([categories, settings]) => {
        setAvailableCategories(categories);
        if (!settings) return;
        setHittingKeys(settings.hittingCategoryKeys);
        setPitchingKeys(settings.pitchingCategoryKeys);
        setRosterSlots(Object.entries(settings.rosterSlots));
      })
      .catch(() => setLoadError("Failed to load scoring categories. Please refresh the page."));
  }, []);

  function toggleCategory(keys: string[], setKeys: (v: string[]) => void, statKey: string, checked: boolean) {
    setKeys(checked ? [...keys, statKey] : keys.filter((k) => k !== statKey));
  }

  function categoryCheckboxes(
    label: string,
    group: "hitting" | "pitching",
    keys: string[],
    setKeys: (v: string[]) => void
  ) {
    return (
      <fieldset>
        <legend>{label}</legend>
        {availableCategories
          .filter((c) => c.group === group)
          .map((category) => (
            <div key={category.statKey}>
              <label htmlFor={`category-${category.statKey}`}>{category.displayName}</label>
              <input
                id={`category-${category.statKey}`}
                type="checkbox"
                checked={keys.includes(category.statKey)}
                onChange={(e) => toggleCategory(keys, setKeys, category.statKey, e.target.checked)}
              />
            </div>
          ))}
      </fieldset>
    );
  }

  function rosterSlotRows() {
    return (
      <fieldset>
        <legend>Roster Slots</legend>
        {rosterSlots.map(([position, count], index) => (
          <div key={index}>
            <label htmlFor={`roster-slot-position-${index}`}>Roster slot position {index}</label>
            <input
              id={`roster-slot-position-${index}`}
              value={position}
              onChange={(e) => {
                const next = [...rosterSlots];
                next[index] = [e.target.value, next[index][1]];
                setRosterSlots(next);
              }}
            />
            <label htmlFor={`roster-slot-count-${index}`}>Roster slot count {index}</label>
            <input
              id={`roster-slot-count-${index}`}
              type="number"
              value={count}
              onChange={(e) => {
                const next = [...rosterSlots];
                next[index] = [next[index][0], Number(e.target.value)];
                setRosterSlots(next);
              }}
            />
            <button type="button" onClick={() => setRosterSlots(rosterSlots.filter((_, i) => i !== index))}>
              Remove
            </button>
          </div>
        ))}
        <button type="button" onClick={() => setRosterSlots([...rosterSlots, ["", 0]])}>
          Add Roster Slot
        </button>
      </fieldset>
    );
  }

  async function handleSave() {
    setSaving(true);
    try {
      const settings: ScoringSettings = {
        hittingCategoryKeys: hittingKeys,
        pitchingCategoryKeys: pitchingKeys,
        rosterSlots: Object.fromEntries(rosterSlots)
      };
      const result = await saveScoringSettings(settings);
      onSaved(result);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div>
      <h1>Scoring Settings</h1>
      {loadError && <p role="alert">{loadError}</p>}
      {categoryCheckboxes("Hitting", "hitting", hittingKeys, setHittingKeys)}
      {categoryCheckboxes("Pitching", "pitching", pitchingKeys, setPitchingKeys)}
      {rosterSlotRows()}
      <button onClick={handleSave} disabled={saving}>
        Save
      </button>
    </div>
  );
}
