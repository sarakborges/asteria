import type { ReactNode } from "react";
import { Text } from "../../atoms/Text/Text";
import "./ScreenShell.css";

export type ScreenShellProps = {
  title: string;
  children: ReactNode;
  background?: ReactNode;
  footer?: ReactNode;
  className?: string;
};

export function ScreenShell({
  title,
  children,
  background,
  footer,
  className,
}: ScreenShellProps) {
  return (
    <main
      className={[
        "screen-shell",
        className,
      ]
        .filter(Boolean)
        .join(" ")}
    >
      {background}
      <header className="screen-shell__header">
        <Text
          text={title}
          variant="screen-title"
        />
      </header>
      <section className="screen-shell__body">
        <div className="screen-shell__content">
          {children}
        </div>
      </section>
      {footer && (
        <footer className="screen-shell__footer">
          {footer}
        </footer>
      )}
    </main>
  );
}
