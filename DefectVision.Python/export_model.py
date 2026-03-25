"""
模型导出脚本
支持导出到 TensorRT、ONNX、OpenVINO 等格式
"""

import argparse
import json
import time
from pathlib import Path


def export_model(
    model_path: str,
    format: str = "engine",
    imgsz: int = 1280,
    half: bool = True,
    device: str = "0",
    dynamic: bool = False,
    batch: int = 1,
    simplify: bool = True,
) -> dict:
    """
    导出 YOLO 模型到指定格式

    Parameters
    ----------
    format : 导出格式
        - 'engine' : TensorRT（推荐，速度最快）
        - 'onnx' : ONNX（通用格式）
        - 'openvino' : OpenVINO（Intel 加速）
        - 'torchscript' : TorchScript

    Returns
    -------
    dict : 导出结果
    """
    from ultralytics import YOLO

    model = YOLO(model_path)

    start = time.time()

    export_path = model.export(
        format=format,
        imgsz=imgsz,
        half=half,
        device=device,
        dynamic=dynamic,
        batch=batch,
        simplify=simplify,
    )

    elapsed = time.time() - start

    result = {
        "source_model": model_path,
        "export_format": format,
        "export_path": str(export_path),
        "imgsz": imgsz,
        "half": half,
        "elapsed_seconds": elapsed,
    }

    return result


def benchmark_model(
    model_path: str,
    data_yaml: str = None,
    imgsz: int = 1280,
    device: str = "0",
    half: bool = True,
    warmup: int = 10,
    runs: int = 100,
) -> dict:
    """
    模型推理速度基准测试

    Returns
    -------
    dict : 包含平均/最小/最大推理时间
    """
    import numpy as np
    from ultralytics import YOLO

    model = YOLO(model_path)

    # 创建随机输入进行预热和测速
    import torch

    dummy_input = torch.randn(1, 3, imgsz, imgsz).to(device)
    if half:
        dummy_input = dummy_input.half()

    # 预热
    print(f"预热 {warmup} 次...")
    for _ in range(warmup):
        model.predict(source=np.random.randint(0, 255, (imgsz, imgsz, 3), dtype=np.uint8),
                      device=device, imgsz=imgsz, verbose=False)

    # 测速
    print(f"测速 {runs} 次...")
    times = []
    for _ in range(runs):
        img = np.random.randint(0, 255, (imgsz, imgsz, 3), dtype=np.uint8)
        t0 = time.time()
        model.predict(source=img, device=device, imgsz=imgsz, verbose=False)
        times.append((time.time() - t0) * 1000)

    return {
        "model_path": model_path,
        "imgsz": imgsz,
        "device": device,
        "half": half,
        "runs": runs,
        "avg_ms": float(np.mean(times)),
        "min_ms": float(np.min(times)),
        "max_ms": float(np.max(times)),
        "std_ms": float(np.std(times)),
        "fps": float(1000.0 / np.mean(times)),
    }


def main():
    parser = argparse.ArgumentParser(description="YOLO Model Export & Benchmark")
    sub = parser.add_subparsers(dest="command")

    # export 子命令
    exp = sub.add_parser("export", help="导出模型")
    exp.add_argument("--model", required=True)
    exp.add_argument("--format", default="engine", choices=["engine", "onnx", "openvino", "torchscript"])
    exp.add_argument("--imgsz", type=int, default=1280)
    exp.add_argument("--half", action="store_true", default=True)
    exp.add_argument("--device", default="0")
    exp.add_argument("--output", default="export_result.json")

    # benchmark 子命令
    bench = sub.add_parser("benchmark", help="速度基准测试")
    bench.add_argument("--model", required=True)
    bench.add_argument("--imgsz", type=int, default=1280)
    bench.add_argument("--device", default="0")
    bench.add_argument("--half", action="store_true", default=True)
    bench.add_argument("--runs", type=int, default=100)
    bench.add_argument("--output", default="benchmark_result.json")

    args = parser.parse_args()

    if args.command == "export":
        result = export_model(
            args.model, args.format, args.imgsz, args.half, args.device
        )
        print(f"导出完成: {result['export_path']}")
        print(f"耗时: {result['elapsed_seconds']:.1f} 秒")

        Path(args.output).write_text(
            json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8"
        )

    elif args.command == "benchmark":
        result = benchmark_model(
            args.model, imgsz=args.imgsz, device=args.device,
            half=args.half, runs=args.runs
        )
        print(f"\n基准测试结果:")
        print(f"  平均: {result['avg_ms']:.2f} ms")
        print(f"  最快: {result['min_ms']:.2f} ms")
        print(f"  最慢: {result['max_ms']:.2f} ms")
        print(f"  FPS:  {result['fps']:.1f}")

        Path(args.output).write_text(
            json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8"
        )
    else:
        parser.print_help()


if __name__ == "__main__":
    main()