// Source: https://magicui.design/r/dock.json (MIT), retrieved 2026-10-11.
// Local changes are limited to the cn import path and formatting.
import React, { useRef, type PropsWithChildren } from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { motion, type MotionValue, useMotionValue, useSpring, useTransform, type MotionProps } from "motion/react";
import { cn } from "../lib/utils";

export interface MagicDockProps extends VariantProps<typeof dockVariants> { className?: string; iconSize?: number; iconMagnification?: number; disableMagnification?: boolean; iconDistance?: number; direction?: "top" | "middle" | "bottom"; children: React.ReactNode; }
const dockVariants = cva("supports-backdrop-blur:bg-white/10 mx-auto mt-8 flex h-[58px] w-max items-center justify-center gap-2 rounded-2xl border p-2 backdrop-blur-md");
export const MagicDock = React.forwardRef<HTMLDivElement, MagicDockProps>(({ className, children, iconSize = 40, iconMagnification = 60, disableMagnification = false, iconDistance = 140, direction = "middle", ...props }, ref) => {
  const mouseX = useMotionValue(Infinity);
  const renderChildren = () => React.Children.map(children, child => React.isValidElement<MagicDockIconProps>(child) && child.type === MagicDockIcon ? React.cloneElement(child, { ...child.props, mouseX, size: iconSize, magnification: iconMagnification, disableMagnification, distance: iconDistance }) : child);
  return <motion.div ref={ref} onMouseMove={e => mouseX.set(e.pageX)} onMouseLeave={() => mouseX.set(Infinity)} {...props} className={cn(dockVariants({ className }), { "items-start": direction === "top", "items-center": direction === "middle", "items-end": direction === "bottom" })}>{renderChildren()}</motion.div>;
});
MagicDock.displayName = "MagicDock";
export interface MagicDockIconProps extends Omit<MotionProps & React.HTMLAttributes<HTMLDivElement>, "children"> { size?: number; magnification?: number; disableMagnification?: boolean; distance?: number; mouseX?: MotionValue<number>; className?: string; children?: React.ReactNode; props?: PropsWithChildren; }
export const MagicDockIcon = ({ size = 40, magnification = 60, disableMagnification, distance = 140, mouseX, className, children, ...props }: MagicDockIconProps) => {
  const ref = useRef<HTMLDivElement>(null); const padding = Math.max(6, size * .2); const defaultMouseX = useMotionValue(Infinity);
  const distanceCalc = useTransform(mouseX ?? defaultMouseX, val => { const bounds = ref.current?.getBoundingClientRect() ?? { x: 0, width: 0 }; return val - bounds.x - bounds.width / 2; });
  const sizeTransform = useTransform(distanceCalc, [-distance, 0, distance], [size, disableMagnification ? size : magnification, size]);
  const scaleSize = useSpring(sizeTransform, { mass: .1, stiffness: 150, damping: 12 });
  return <motion.div ref={ref} style={{ width: scaleSize, height: scaleSize, padding }} className={cn("flex aspect-square cursor-pointer items-center justify-center rounded-full", disableMagnification && "transition-colors hover:bg-slate-100", className)} {...props}><div>{children}</div></motion.div>;
};
MagicDockIcon.displayName = "MagicDockIcon";
