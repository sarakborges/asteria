import { useCallback, useId, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import "./ScrollArea.css";

type Metrics = { height: number; scrollHeight: number; scrollTop: number };

export type ScrollAreaProps = {
  children: ReactNode;
  className?: string;
  contentClassName?: string;
  ariaLabel: string;
};

/**
 * Thin presentation adapter over browser-native scrolling: the viewport owns
 * wheel, touch and keyboard semantics. Only the visual track is custom.
 */
export function ScrollArea({ children, className, contentClassName, ariaLabel }: ScrollAreaProps) {
  const viewportId = useId();
  const viewportRef = useRef<HTMLDivElement>(null);
  const contentRef = useRef<HTMLDivElement>(null);
  const dragRef = useRef<{ pointer: number; y: number; top: number } | null>(null);
  const [metrics, setMetrics] = useState<Metrics>({ height: 0, scrollHeight: 0, scrollTop: 0 });

  const measure = useCallback(() => {
    const element = viewportRef.current;
    if (!element) return;
    const next = {
      height: element.clientHeight,
      scrollHeight: element.scrollHeight,
      scrollTop: element.scrollTop,
    };
    setMetrics(previous =>
      previous.height === next.height &&
      previous.scrollHeight === next.scrollHeight &&
      previous.scrollTop === next.scrollTop ? previous : next);
  }, []);

  useLayoutEffect(() => {
    const viewport = viewportRef.current;
    const content = contentRef.current;
    if (!viewport || !content) return;
    const observer = new ResizeObserver(measure);
    observer.observe(viewport);
    observer.observe(content);
    measure();
    return () => observer.disconnect();
  }, [measure]);

  const overflow = metrics.scrollHeight - metrics.height;
  const hasScrollbar = overflow > 0.5 && metrics.height > 0;
  const thumbHeight = hasScrollbar
    ? Math.min(metrics.height, Math.max(28, metrics.height * metrics.height / metrics.scrollHeight))
    : 0;
  const available = Math.max(0, metrics.height - thumbHeight);
  const thumbTop = overflow > 0 ? metrics.scrollTop / overflow * available : 0;

  return (
    <div className={["ui-scroll-area", hasScrollbar ? "ui-scroll-area--overflow" : "",
      className].filter(Boolean).join(" ")}>
      <div id={viewportId} ref={viewportRef}
        className="ui-scroll-area__viewport"
        role="region" aria-label={ariaLabel} tabIndex={0}
        onScroll={measure}>
        <div ref={contentRef} className={["ui-scroll-area__content", contentClassName]
          .filter(Boolean).join(" ")}>{children}</div>
      </div>
      {hasScrollbar && (
        <div className="ui-scroll-area__track" aria-hidden="true"
          onPointerDown={event => {
            if (event.target !== event.currentTarget || !viewportRef.current) return;
            const bounds = event.currentTarget.getBoundingClientRect();
            viewportRef.current.scrollTop = (event.clientY - bounds.top - thumbHeight / 2)
              / Math.max(1, available) * overflow;
            measure();
          }}>
          <div className="ui-scroll-area__thumb"
            style={{ height: thumbHeight, transform: `translateY(${thumbTop}px)` }}
            onPointerDown={event => {
              if (event.button !== 0 || !viewportRef.current) return;
              event.preventDefault();
              event.stopPropagation();
              dragRef.current = {
                pointer: event.pointerId, y: event.clientY,
                top: viewportRef.current.scrollTop,
              };
              event.currentTarget.setPointerCapture(event.pointerId);
            }}
            onPointerMove={event => {
              const drag = dragRef.current;
              if (!drag || drag.pointer !== event.pointerId || !viewportRef.current) return;
              viewportRef.current.scrollTop = drag.top +
                (event.clientY - drag.y) * overflow / Math.max(1, available);
            }}
            onPointerUp={event => {
              if (dragRef.current?.pointer !== event.pointerId) return;
              dragRef.current = null;
              event.currentTarget.releasePointerCapture(event.pointerId);
            }}
            onPointerCancel={() => { dragRef.current = null; }}
          />
        </div>
      )}
    </div>
  );
}
