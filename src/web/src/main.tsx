import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { App } from "./app/App";
import { IdentityProvider } from "./identity/IdentityProvider";
import { SessionProvider } from "./session/SessionProvider";
import "./styles.css";

const root = document.getElementById("root");
if (!root) {
  throw new Error("The page has no #root element.");
}

createRoot(root).render(
  <StrictMode>
    <IdentityProvider>
      <SessionProvider>
        <BrowserRouter>
          <App />
        </BrowserRouter>
      </SessionProvider>
    </IdentityProvider>
  </StrictMode>,
);
