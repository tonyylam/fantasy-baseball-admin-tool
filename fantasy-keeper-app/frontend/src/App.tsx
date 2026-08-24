import { useAuth } from "./state/useAuth";
import { PinEntryScreen } from "./screens/PinEntryScreen";
import { KeeperFormScreen } from "./screens/KeeperFormScreen";
import { AdminPanel } from "./screens/AdminPanel";

export default function App() {
  const { pin, auth, login, logout, error, isLoading } = useAuth();

  if (!pin || !auth) {
    return <PinEntryScreen onSubmit={login} error={error} isLoading={isLoading} />;
  }

  return (
    <div>
      <button onClick={logout}>Log out</button>
      {auth.role === "Admin" ? (
        <AdminPanel pin={pin} />
      ) : (
        <KeeperFormScreen pin={pin} />
      )}
    </div>
  );
}
