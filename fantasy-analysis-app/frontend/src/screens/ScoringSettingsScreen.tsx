import { useEffect, useState } from "react";
import { getScoringSettings, saveScoringSettings } from "../api/client";
import type { ScoringCategory, ScoringSettings } from "../types";

interface ScoringSettingsScreenProps {
  onSaved: (settings: ScoringSettings) => void;
}

export function ScoringSettingsScreen({ onSaved }: ScoringSettingsScreenProps) {
  const [hitting, setHitting] = useState<ScoringCategory[]>([]);
  const [pitching, setPitching] = useState<ScoringCategory[]>([]);
  const [rosterSlots, setRosterSlots] = useState<[string, number][]>([]);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    getScoringSettings().then((settings) => {
      if (!settings) return;
      setHitting(settings.hittingCategories);
      setPitching(settings.pitchingCategories);
      setRosterSlots(Object.entries(settings.rosterSlots));
    });
  }, []);

  function updateCategory(
    list: ScoringCategory[],
    setList: (v: ScoringCategory[]) => void,
    index: number,
    field: keyof ScoringCategory,
    value: string
  ) {
    const next = [...list];
    next[index] = { ...next[index], [field]: field === "pointsPerUnit" ? Number(value) : value };
    setList(next);
  }

  function categoryRows(
    label: string,
    list: ScoringCategory[],
    setList: (v: ScoringCategory[]) => void
  ) {
    return (
      <fieldset>
        <legend>{label}</legend>
        {list.map((category, index) => (
          <div key={index}>
            <label htmlFor={`${label}-key-${index}`}>{label} stat key {index}</label>
            <input
              id={`${label}-key-${index}`}
              value={category.statKey}
              onChange={(e) => updateCategory(list, setList, index, "statKey", e.target.value)}
            />
            <label htmlFor={`${label}-points-${index}`}>{label} points {index}</label>
            <input
              id={`${label}-points-${index}`}
              type="number"
              value={category.pointsPerUnit}
              onChange={(e) => updateCategory(list, setList, index, "pointsPerUnit", e.target.value)}
            />
            <button type="button" onClick={() => setList(list.filter((_, i) => i !== index))}>
              Remove
            </button>
          </div>
        ))}
        <button type="button" onClick={() => setList([...list, { statKey: "", pointsPerUnit: 0 }])}>
          Add {label} Category
        </button>
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
        hittingCategories: hitting,
        pitchingCategories: pitching,
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
      {categoryRows("Hitting", hitting, setHitting)}
      {categoryRows("Pitching", pitching, setPitching)}
      {rosterSlotRows()}
      <button onClick={handleSave} disabled={saving}>
        Save
      </button>
    </div>
  );
}
