import { useState, type FormEvent } from "react";

interface Props {
  onSubmit: (pin: string) => void;
  error: string | null;
  isLoading: boolean;
}

export function PinEntryScreen({ onSubmit, error, isLoading }: Props) {
  const [pin, setPin] = useState("");

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (pin.trim()) {
      onSubmit(pin.trim());
    }
  }

  return (
    <form onSubmit={handleSubmit} className="pin-entry">
      <h1>Worm Burners Keepers</h1>
      <label htmlFor="pin">Enter your team PIN</label>
      <input
        id="pin"
        type="password"
        inputMode="numeric"
        value={pin}
        onChange={(event) => setPin(event.target.value)}
        autoFocus
      />
      <button type="submit" disabled={isLoading || !pin.trim()}>
        {isLoading ? "Checking..." : "Continue"}
      </button>
      {error && <p role="alert">{error}</p>}
    </form>
  );
}
