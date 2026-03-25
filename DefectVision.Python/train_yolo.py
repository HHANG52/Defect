"""
YOLO 训练入口脚本
支持从 C# 通过 Python.NET 调用，也可以命令行独立运行
提供训练回调，实时向 C# 层汇报进度
"""

import argparse
import json
import sys
import time
from pathlib import Path
from typing import Callable, Optional


class TrainingCallback:
    """
    训练回调：每个 epoch 结束时调用外部 callback 函数
    用于向 C# 层汇报训练进度
    """

    def __init__(self, total_epochs: int, callback: Optional[Callable] = None):
        self.total_epochs = total_epochs
        self.callback = callback
        self.start_time = time.time()
        self.best_map50 = 0.0
        self.history = []

    def on_train_epoch_end(self, trainer):
        """每个训练 epoch 结束"""
        epoch = trainer.epoch + 1
        metrics = {
            "epoch": epoch,
            "total_epochs": self.total_epochs,
            "box_loss": float(trainer.loss_items[0]) if trainer.loss_items is not None else 0,
            "cls_loss": float(trainer.loss_items[1]) if trainer.loss_items is not None else 0,
            "dfl_loss": float(trainer.loss_items[2]) if trainer.loss_items is not None else 0,
            "lr": float(trainer.optimizer.param_groups[0]["lr"]),
            "elapsed_seconds": time.time() - self.start_time,
        }

        if self.callback:
            self.callback("epoch_train_end", metrics)

    def on_val_end(self, validator):
        """每次验证结束"""
        try:
            metrics = {
                "map50": float(validator.metrics.box.map50),
                "map5095": float(validator.metrics.box.map),
                "precision": float(validator.metrics.box.mp),
                "recall": float(validator.metrics.box.mr),
            }

            if metrics["map50"] > self.best_map50:
                self.best_map50 = metrics["map50"]
                metrics["is_best"] = True
            else:
                metrics["is_best"] = False

            self.history.append(metrics)

            if self.callback:
                self.callback("val_end", metrics)
        except Exception as e:
            print(f"[TrainingCallback] val_end error: {e}")

    def on_train_end(self, trainer):
        """训练结束"""
        summary = {
            "best_map50": self.best_map50,
            "total_epochs": trainer.epoch + 1,
            "elapsed_seconds": time.time() - self.start_time,
            "best_model_path": str(trainer.best),
            "last_model_path": str(trainer.last),
            "history": self.history,
        }

        if self.callback:
            self.callback("train_end", summary)


def train(
    data_yaml: str,
    base_model: str = "yolo11m.pt",
    epochs: int = 200,
    imgsz: int = 1280,
    batch: int = 8,
    device: str = "0",
    lr0: float = 0.01,
    patience: int = 50,
    workers: int = 4,
    augment: bool = True,
    mosaic: float = 1.0,
    mixup: float = 0.1,
    scale: float = 0.5,
    project: str = "runs/train",
    name: str = "exp",
    resume: bool = False,
    callback: Optional[Callable] = None,
) -> dict:
    """
    执行 YOLO 训练

    Parameters
    ----------
    data_yaml : 数据集 YAML 路径
    base_model : 基础模型路径（预训练或续训模型）
    callback : 回调函数 callback(event_name, metrics_dict)

    Returns
    -------
    dict : 训练结果摘要
    """
    from ultralytics import YOLO

    model = YOLO(base_model)

    # 注册回调
    tc = TrainingCallback(epochs, callback)
    model.add_callback("on_train_epoch_end", tc.on_train_epoch_end)
    model.add_callback("on_val_end", tc.on_val_end)
    model.add_callback("on_train_end", tc.on_train_end)

    # 开始训练
    results = model.train(
        data=data_yaml,
        epochs=epochs,
        imgsz=imgsz,
        batch=batch,
        device=device,
        lr0=lr0,
        patience=patience,
        workers=workers,
        augment=augment,
        mosaic=mosaic,
        mixup=mixup,
        scale=scale,
        project=project,
        name=name,
        exist_ok=True,
        resume=resume,
        verbose=True,
    )

    return {
        "best_model_path": str(model.trainer.best),
        "last_model_path": str(model.trainer.last),
        "best_map50": tc.best_map50,
        "history": tc.history,
    }


def main():
    parser = argparse.ArgumentParser(description="YOLO Training Script")
    parser.add_argument("--data", required=True, help="数据集 YAML 路径")
    parser.add_argument("--model", default="yolo11m.pt", help="基础模型")
    parser.add_argument("--epochs", type=int, default=200)
    parser.add_argument("--imgsz", type=int, default=1280)
    parser.add_argument("--batch", type=int, default=8)
    parser.add_argument("--device", default="0")
    parser.add_argument("--lr0", type=float, default=0.01)
    parser.add_argument("--patience", type=int, default=50)
    parser.add_argument("--workers", type=int, default=4)
    parser.add_argument("--project", default="runs/train")
    parser.add_argument("--name", default="exp")
    parser.add_argument("--resume", action="store_true")
    parser.add_argument("--output", default="train_result.json", help="结果输出路径")
    args = parser.parse_args()

    def print_callback(event, metrics):
        if event == "val_end":
            m = metrics
            print(
                f"  mAP50={m['map50']:.4f} "
                f"P={m['precision']:.4f} R={m['recall']:.4f}"
                f"{' ★ BEST' if m.get('is_best') else ''}"
            )
        elif event == "train_end":
            print(f"\n训练完成！最佳 mAP@50: {metrics['best_map50']:.4f}")
            print(f"最佳模型: {metrics['best_model_path']}")

    result = train(
        data_yaml=args.data,
        base_model=args.model,
        epochs=args.epochs,
        imgsz=args.imgsz,
        batch=args.batch,
        device=args.device,
        lr0=args.lr0,
        patience=args.patience,
        workers=args.workers,
        project=args.project,
        name=args.name,
        resume=args.resume,
        callback=print_callback,
    )

    Path(args.output).write_text(
        json.dumps(result, ensure_ascii=False, indent=2, default=str),
        encoding="utf-8",
    )
    print(f"结果已保存: {args.output}")


if __name__ == "__main__":
    main()